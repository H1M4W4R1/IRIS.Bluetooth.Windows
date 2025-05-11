using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using IRIS.Bluetooth.Common;
using IRIS.Bluetooth.Common.Abstract;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using IRIS.Bluetooth.Common.Data;

namespace IRIS.Bluetooth.Windows.Structure
{
    internal sealed class WindowsBluetoothLECharacteristic(IBluetoothLEService service,
        GattCharacteristic characteristic
    )
        : IBluetoothLECharacteristic
    {
        /// <summary>
        ///     Gatt characteristic that this characteristic is associated with
        /// </summary>
        internal GattCharacteristic? GattCharacteristic { get; init; } = characteristic;

        public IBluetoothLEService Service { get; } = service;
        public string UUID { get; } = characteristic.Uuid.ToString();

        public bool IsRead
            => GattCharacteristic?.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read) == true;

        public bool IsWrite
            => GattCharacteristic?.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse) == true;

        public bool IsNotify
            => GattCharacteristic?.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify) == true;

        public event CharacteristicValueChanged? ValueChanged;

        /// <summary>
        ///     Check if the characteristic is available
        /// </summary>
        [MemberNotNullWhen(true, nameof(GattCharacteristic))] public bool IsAvailable
            => GattCharacteristic is not null;

        internal async ValueTask<byte[]?> ReadAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Check if the GattCharacteristic is null
                if (!IsAvailable) return null;

                // Attempt to read the value of the characteristic
                GattReadResult result = await GattCharacteristic.ReadValueAsync(BluetoothCacheMode.Uncached)
                    .AsTask(cancellationToken);
                if (result.Status != GattCommunicationStatus.Success) return null;

                byte[] data = new byte[result.Value.Length];
                using DataReader reader = DataReader.FromBuffer(result.Value);
                reader.ReadBytes(data);

                return data;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                return null;
            }
        }

        ValueTask<bool> IBluetoothLECharacteristic.WriteAsync(byte[] data, CancellationToken cancellationToken) =>
            WriteAsync(data, cancellationToken);

        ValueTask<byte[]?> IBluetoothLECharacteristic.ReadAsync(CancellationToken cancellationToken) =>
            ReadAsync(cancellationToken);

        internal async ValueTask<bool> WriteAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!IsAvailable) return false;

                using DataWriter writer = new();
                writer.WriteBytes(data);
                GattCommunicationStatus status = await GattCharacteristic
                    .WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse)
                    .AsTask(cancellationToken);

                return status == GattCommunicationStatus.Success;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                return false;
            }
        }

        public async ValueTask<bool> SubscribeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!IsAvailable) return false;

                GattCommunicationStatus status = await GattCharacteristic
                    .WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Notify)
                    .AsTask(cancellationToken);

                if (status != GattCommunicationStatus.Success) return false;

                GattCharacteristic.ValueChanged += OnValueChanged;
                return true;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                return false;
            }
        }

        public async ValueTask<bool> UnsubscribeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!IsAvailable) return false;

                GattCommunicationStatus status = await GattCharacteristic
                    .WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None)
                    .AsTask(cancellationToken);

                if (status != GattCommunicationStatus.Success) return false;

                GattCharacteristic.ValueChanged -= OnValueChanged;
                return true;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                return false;
            }
        }

        private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            // Get byte data from buffer
            byte[] data = new byte[args.CharacteristicValue.Length];
            using DataReader reader = DataReader.FromBuffer(args.CharacteristicValue);
            reader.ReadBytes(data);

            // Raise the ValueChanged event
            ValueChanged?.Invoke(this, data);
        }
    }
}