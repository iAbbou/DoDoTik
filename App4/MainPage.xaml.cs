using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Threading.Tasks;

namespace App4
{
    public class BluetoothCommunication
    {
        public event EventHandler<string> DataReceived;

        public void SimulateDataReceived(string data)
        {
            DataReceived?.Invoke(this, data);
        }
    }
    public sealed partial class MainPage : Page
    {
        private ObservableCollection<BluetoothDevice> deviceList;
        private HashSet<string> discoveredDeviceIds;
        private DeviceWatcher deviceWatcher;
        private SerialDevice serialPort;
        private BluetoothLEDevice connectedDevice;
        private GattCharacteristic dataCharacteristic;
        private BluetoothCommunication bluetoothCommunication = new BluetoothCommunication();
        private SerialDevice serialDevice;
        private DataReader dataReader;
        private ObservableCollection<string> receivedDataList = new ObservableCollection<string>();

        public MainPage()
        {
            this.InitializeComponent();
            deviceList = new ObservableCollection<BluetoothDevice>();
            DeviceListBox.ItemsSource = deviceList;
            discoveredDeviceIds = new HashSet<string>();
            ReceivedDataListBox.Items.Add("HI");
            StartReceivingData();
            StartDeviceWatcher();
        }


        private void StartDeviceWatcher()
        {
            string deviceSelector = BluetoothLEDevice.GetDeviceSelector();

            deviceWatcher = DeviceInformation.CreateWatcher(deviceSelector, null);

            deviceWatcher.Added += async (sender, args) =>
            {
                try
                {
                    if (!discoveredDeviceIds.Contains(args.Id))
                    {
                        DeviceInformation deviceInfo = await DeviceInformation.CreateFromIdAsync(args.Id);

                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                        {
                            var bluetoothDevice = new BluetoothDevice
                            {
                                DeviceName = args.Name,
                                DeviceId = args.Id
                            };

                            bluetoothDevice.DeviceReference = await BluetoothLEDevice.FromIdAsync(args.Id);

                            bluetoothDevice.IsPaired = deviceInfo.Pairing?.IsPaired ?? false;

                            deviceList.Add(bluetoothDevice);
                            deviceList = new ObservableCollection<BluetoothDevice>(deviceList.OrderBy(name => name.DeviceName));
                            DeviceListBox.ItemsSource = deviceList;
                        });

                        discoveredDeviceIds.Add(args.Id);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in Added event handler: {ex.Message}");
                }
            };

            deviceWatcher.Removed += (sender, args) =>
            {
                try
                {
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in Removed event handler: {ex.Message}");
                }
            };

            if (deviceWatcher.Status == DeviceWatcherStatus.Created)
            {
                deviceWatcher.Start();
            }
        }


        private async void ConnectToBluetoothDevice()
        {
            string deviceId = "COM3"; 

            var deviceInfo = await DeviceInformation.CreateFromIdAsync(deviceId);
            serialDevice = await SerialDevice.FromIdAsync(deviceId);

            if (serialDevice != null)
            {
                serialDevice.BaudRate = 9600; 
                serialDevice.DataBits = 8;
                serialDevice.StopBits = SerialStopBitCount.One;
                serialDevice.Parity = SerialParity.None;

                dataReader = new DataReader(serialDevice.InputStream);

            
            }
            else
            {
                Debug.WriteLine("Failed to connect to the Bluetooth device.");
            }
        }

        private void UpdateDeviceList(string deviceId, BluetoothDevice deviceName)
        {
            Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                deviceList.Remove(deviceName);
            });

            discoveredDeviceIds.Remove(deviceId);
        }

        private ulong BluetoothAddressToUlong(string bluetoothAddress)
        {
            return ulong.Parse(bluetoothAddress.Replace(":", ""), System.Globalization.NumberStyles.HexNumber);
        }

        private Guid ConvertToGuid(string hexString)
        {
            if (hexString.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                hexString = hexString.Substring(2);
            }

            hexString = hexString.PadLeft(32, '0');

            return Guid.ParseExact(hexString, "N");
        }

        private async void DeviceListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DeviceListBox.SelectedItem is BluetoothDevice selectedDevice)
            {
                try
                {
                    connectedDevice = await BluetoothLEDevice.FromIdAsync(selectedDevice.DeviceReference.DeviceId);

                    if (connectedDevice != null)
                    {
                        if (!connectedDevice.DeviceInformation.Pairing.IsPaired)
                        {
                            DevicePairingResult pairingResult = await connectedDevice.DeviceInformation.Pairing.PairAsync();

                            if (pairingResult.Status == DevicePairingResultStatus.Paired)
                            {
                                Debug.WriteLine("Device paired successfully.");
                                ConnectToBluetoothDevice();
                                ConnectedDevice.Text = $"Connected Device: {selectedDevice.DeviceName}";

                                StartReceivingData();
                            }
                            else
                            {
                                Debug.WriteLine($"Failed to pair the device. Status: {pairingResult.Status}");
                                Status.Text = $"Failed to pair the device. Status: {pairingResult.Status}";
                                return; // Exit if pairing fails
                            }
                        }

                        var servicesResult = await connectedDevice.GetGattServicesAsync();
                        var service = servicesResult.Services.FirstOrDefault(s => s.Uuid == new Guid("0000ffe0-0000-1000-8000-00805f9b34fb"));

                        if (service != null)
                        {
                            var characteristicsResult = await service.GetCharacteristicsAsync();
                            dataCharacteristic = characteristicsResult.Characteristics.FirstOrDefault(c => c.Uuid == new Guid("0000ffe1-0000-1000-8000-00805f9b34fb"));

                            if (dataCharacteristic != null)
                            {
                                Debug.WriteLine("Device connected and characteristic found.");
                                StartReceivingData();
                                ConnectedDevice.Text = $"Connected Device: {selectedDevice.DeviceName}";
                            }
                            else
                            {
                                Debug.WriteLine("Characteristic not found.");
                                Status.Text = "Characteristic Not Found";
                            }
                        }
                        else
                        {
                            Debug.WriteLine("Service not found.");
                            Status.Text = "Service Not Found";
                        }
                    }
                    else
                    {
                        Debug.WriteLine("Device not connected.");
                        Status.Text = "Device Not Connected";
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error connecting to the device: {ex.Message}");
                    Status.Text = $"Error: {ex.Message}";
                }
            }
        }
        private async void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            if (connectedDevice != null && dataCharacteristic != null)
            {
                try
                {
                    // Get data from TempTextBox and validate
                    string tempData = TempTextBox.Text;
                    if (!string.IsNullOrEmpty(tempData))
                    {
                        await SendData(tempData);
                    }
                    else
                    {
                        Validation.Text = "Failed: TempTextBox is empty";
                        Validation.Foreground = new SolidColorBrush(Windows.UI.Colors.Red);

                        return;
                    }

                    // Get data from HumTextBox and validate
                    string humData = HumTextBox.Text;
                    if (!string.IsNullOrEmpty(humData))
                    {
                        await SendData(humData);
                    }
                    else
                    {
                        Validation.Text = "Failed: HumTextBox is empty";
                        Validation.Foreground = new SolidColorBrush(Windows.UI.Colors.Red);
                        return;
                    }

                    Validation.Text = "Data Sent Successfully";
                    Validation.Foreground = new SolidColorBrush(Windows.UI.Colors.Green);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error sending data: {ex.Message}");
                    Validation.Text = $"Error: {ex.Message}";
                    Validation.Foreground = new SolidColorBrush(Windows.UI.Colors.Red);

                }
            }
            else
            {
                Debug.WriteLine("Device or characteristic is not available.");
                Validation.Text = "Device or characteristic not available.";
                Validation.Foreground = new SolidColorBrush(Windows.UI.Colors.Red);

            }
        }

        private async Task SendData(string data)
        {
            var dataWriter = new Windows.Storage.Streams.DataWriter();
            dataWriter.WriteString(data);

            await dataCharacteristic.WriteValueAsync(dataWriter.DetachBuffer());
        }


        private async void StartReceivingData()
        {
            if (connectedDevice != null)
            {
                try
                {
                    var servicesResult = await connectedDevice.GetGattServicesAsync();
                    var service = servicesResult.Services.FirstOrDefault(s => s.Uuid == new Guid("0000ffe0-0000-1000-8000-00805f9b34fb"));

                    if (service != null)
                    {
                        var characteristicsResult = await service.GetCharacteristicsAsync();
                        dataCharacteristic = characteristicsResult.Characteristics.FirstOrDefault(c => c.Uuid == new Guid("0000ffe1-0000-1000-8000-00805f9b34fb"));

                        if (dataCharacteristic != null)
                        {
                            await dataCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                            dataCharacteristic.ValueChanged += DataCharacteristic_ValueChanged;
                        }
                        else
                        {
                            Debug.WriteLine("Characteristic not found.");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("Service not found.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error connecting to the device: {ex.Message}");
                }
            }
            else
            {
                Debug.WriteLine("No connected device");
            }
        }

        private async void DataCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            using (DataReader dataReader = DataReader.FromBuffer(args.CharacteristicValue))
            {
                string data = dataReader.ReadString(args.CharacteristicValue.Length);

                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    DisplayReceivedData(data);
                });
            }
        }

        private void DisplayReceivedData(string data)
        {
            ReceivedDataListBox.Items.Add(data);
        
        }
        private void TextBlock_SelectionChanged(object sender, RoutedEventArgs e)
        {

        }
        private void TextBlock_SelectionChanged_1(object sender, RoutedEventArgs e)
        {

        }
        private void ReceivedDataListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }


    }



}
