using System.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using IRIS.Bluetooth.Common;
using IRIS.Bluetooth.Common.Abstract;
using IRIS.Bluetooth.Common.Addressing;
using IRIS.Bluetooth.Windows.Structure;
using IRIS.Communication;

namespace IRIS.Bluetooth.Windows.Communication
{
    public sealed class WindowsBluetoothLEInterface(IBluetoothLEAddress deviceAddress) : IBluetoothLEInterface
    {
        private readonly List<IBluetoothLEDevice> _connectedDevices = new();
        private readonly List<IBluetoothLEDevice> _discoveredDevices = new();

        /// <summary>
        ///     List of all discovered devices
        /// </summary>
        public IReadOnlyList<IBluetoothLEDevice> DiscoveredDevices
        {
            get
            {
                lock (_discoveredDevices) return _discoveredDevices;
            }
        }

        /// <summary>
        ///     List of all connected devices
        /// </summary>
        public IReadOnlyList<IBluetoothLEDevice> ConnectedDevices
        {
            get
            {
                lock (_connectedDevices) return _connectedDevices;
            }
        }

        public IBluetoothLEDevice? ClaimDevice(CancellationToken cancellationToken = default)
        {
            // Get any discovered device that is not connected
            IBluetoothLEDevice? device;

            lock (_discoveredDevices)
            {
                lock (_connectedDevices)
                {
                    device = _discoveredDevices.FirstOrDefault(d => !_connectedDevices.Contains(d));
                }
            }

            // Check if device is null
            if (device != null)
            {
                RegisterDevice(device);
                return device;
            }

            // Register handler to detect device discovery
            OnBluetoothDeviceDiscovered += OnDeviceDiscovered;

            // Device is changed via the event handler
            while (device == null)
            {
                // Handle cancellation token
                if (cancellationToken.IsCancellationRequested) break;
            }

            // Unregister handler
            OnBluetoothDeviceDiscovered -= OnDeviceDiscovered;

            // Returns null if cancellation was requested or device that was discovered
            return device;

            //
            void OnDeviceDiscovered(IBluetoothLEInterface sender, IBluetoothLEDevice deviceInstance)
            {
                RegisterDevice(deviceInstance);
            }

            void RegisterDevice(IBluetoothLEDevice deviceInstance)
            {
                // Add device to the list
                lock (_connectedDevices)
                {
                    if (_connectedDevices.Contains(deviceInstance)) return;
                    _connectedDevices.Add(deviceInstance);
                }

                // Call notifications
                OnBluetoothDeviceConnected?.Invoke(this, deviceInstance);
                DeviceConnected?.Invoke(DeviceBluetoothAddress);

                // Update the device
                device = deviceInstance;
            }
        }

        public void ReleaseDevice(IBluetoothLEDevice device)
        {
            // Check if device is connected
            lock (_connectedDevices)
            {
                if (!_connectedDevices.Contains(device)) return;

                // Remove device from the list
                _connectedDevices.Remove(device);
            }

            // Call notifications
            OnBluetoothDeviceDisconnected?.Invoke(this, device);
            DeviceDisconnected?.Invoke(DeviceBluetoothAddress);
        }

        public event DeviceDiscovered? OnBluetoothDeviceDiscovered;
        public event DeviceConnected? OnBluetoothDeviceConnected;
        public event DeviceDisconnected? OnBluetoothDeviceDisconnected;
        public event DeviceConnectionLost? OnBluetoothDeviceConnectionLost;

        /// <summary>
        ///     Address of the device that this interface is searching for
        /// </summary>
        public IBluetoothLEAddress DeviceBluetoothAddress { get; } = deviceAddress;

        /// <summary>
        ///     Check if the interface is connected to a device
        /// </summary>
        public bool IsConnected
        {
            get
            {
                lock (_connectedDevices) return _connectedDevices.Count > 0;
            }
        }

        /// <summary>
        ///     Check if the interface is currently scanning for devices
        /// </summary>
        private bool IsRunning { get; set; }

        /// <summary>
        /// Device watcher for scanning for devices
        /// </summary>
        private readonly BluetoothLEAdvertisementWatcher _watcher = new()
        {
            ScanningMode = BluetoothLEScanningMode.Active,
            AdvertisementFilter = new BluetoothLEAdvertisementFilter
            {
                Advertisement = new BluetoothLEAdvertisement()
            },
            SignalStrengthFilter = new BluetoothSignalStrengthFilter
            {
                InRangeThresholdInDBm = -75,
                OutOfRangeThresholdInDBm = -70,
                OutOfRangeTimeout = TimeSpan.FromSeconds(2)
            }
        };

        public bool Connect(CancellationToken cancellationToken = default)
        {
            // If interface is already running a scan - we don't need to start a new one
            if (IsRunning) return true;

            // Start scanning for devices
            _watcher.Received += OnAdvertisementReceived;
            _watcher.Stopped += OnWatcherStopped;
            try
            {
                _watcher.Start();
                IsRunning = true;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }

            return true;
        }

        public bool Disconnect()
        {
            if (!IsRunning) return true;

            // Disconnect all devices and clear both lists
            lock (_connectedDevices)
            {
                // Perform all notifications necessary
                foreach (IBluetoothLEDevice device in _connectedDevices)
                {
                    // Device was disconnected intentionally, keep that for reference
                    OnBluetoothDeviceConnectionLost?.Invoke(this, device);
                }

                // Clear the list
                _connectedDevices.Clear();
            }

            // Clear the discovered devices list
            lock (_discoveredDevices)
            {
                _discoveredDevices.Clear();
            }

            // Stop scanning for devices
            _watcher.Received -= OnAdvertisementReceived;
            try
            {
                _watcher.Stop();
                IsRunning = false;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }

            return true;
        }

        private void OnWatcherStopped(
            BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            // Disconnect as we've failed scanning for some reason
            Disconnect();
        }

        private void OnAdvertisementReceived(
            BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementReceivedEventArgs args)
        {
            // Check if device is already discovered
            lock (_discoveredDevices)
            {
                if (_discoveredDevices.Any(device => device.DeviceAddress == args.BluetoothAddress)) return;

                // Get device from Bluetooth address
                BluetoothLEDevice device =
                    BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress).AsTask().Result;

                // Check if device is null
                if (device == null) return;

                Debug.WriteLine($"Discovered device: {device.Name} ({device.BluetoothAddress})");

                // Create new instance of the device
                WindowsBluetoothLEDevice bluetoothDevice = new(device);

                // Check if desired address is valid for this device
                if (!DeviceBluetoothAddress.IsDeviceValid(bluetoothDevice))
                {
                    Debug.WriteLine(
                        $"Device {bluetoothDevice.Name} ({bluetoothDevice.DeviceAddress}) is not valid for address {DeviceBluetoothAddress}");
                    return;
                }

                // Add device to the list - it was successfully discovered 
                // also perform all notifications necessary
                _discoveredDevices.Add(bluetoothDevice);
                device.ConnectionStatusChanged += OnDeviceConnectionStatusChanged;
                OnBluetoothDeviceDiscovered?.Invoke(this, bluetoothDevice);
            }
        }

        private void OnDeviceConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            // Check status - we look for disconnected devices
            if (sender.ConnectionStatus != BluetoothConnectionStatus.Disconnected) return;

            // Notify that device connection was lost
            AttemptToDisconnectDevice(sender);

            // Remove device from the lists and perform notifications in safe manner
            lock (_discoveredDevices)
            {
                lock (_connectedDevices)
                {
                    // Remove device from the lists
                    _connectedDevices.RemoveAll(device => device.DeviceAddress == sender.BluetoothAddress);
                    _discoveredDevices.RemoveAll(device => device.DeviceAddress == sender.BluetoothAddress);

                    // Remove device from the list
                    sender.ConnectionStatusChanged -= OnDeviceConnectionStatusChanged;
                    sender.Dispose();
                }
            }
        }

        private void AttemptToDisconnectDevice(BluetoothLEDevice sender)
        {
            // Check if device is connected
            lock (_discoveredDevices)
            {
                IBluetoothLEDevice? device =
                    _discoveredDevices.FirstOrDefault(device => device.DeviceAddress == sender.BluetoothAddress);

                // Check if device was discovered
                if (device == null) return;
                
                // Connection to discovered device was lost
                OnBluetoothDeviceConnectionLost?.Invoke(this, device);
            }
        }


        // Those events are trash, but they are here for compatibility with the rest of the API
        public event Delegates.DeviceConnectedHandler<IBluetoothLEAddress>? DeviceConnected;
        public event Delegates.DeviceDisconnectedHandler<IBluetoothLEAddress>? DeviceDisconnected;
        public event Delegates.DeviceConnectionLostHandler<IBluetoothLEAddress>? DeviceConnectionLost;
    }
}