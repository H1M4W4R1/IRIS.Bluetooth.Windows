using System.Diagnostics;

namespace IRIS.Bluetooth.Devices
{
    /// <summary>
    /// Bluetooth LE Device that has loop to perform time-based actions
    /// </summary>
    public abstract class BluetoothLowEnergyLoopDeviceBase : BluetoothLowEnergyDeviceBase
    {
        /// <summary>
        /// Used to cancel the loop while disposing the device
        /// </summary>
        protected readonly CancellationTokenSource deviceLoopCancellationTokenSource = new();
        
        /// <summary>
        /// Indicates if the loop should throw exceptions
        /// </summary>
        protected virtual bool ThrowLoopExceptions => false;
        
        /// <summary>
        /// Actions that shall be performed in the loop
        /// </summary>
        protected abstract Task OnDeviceLoop(CancellationToken cancellationToken = default);
        
        private async void StartDeviceLoop(CancellationToken cancellationToken = default)
        {
            // Perform loop actions here
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Check if the device is connected
                    if (!IsConnected)
                    {
                        // Wait and ignore everything else
                        await Task.Delay(25, cancellationToken);
                        continue;
                    }

                    // Call the loop method
                    await OnDeviceLoop(cancellationToken);
                }
                catch (Exception anyException)
                {
                    // Check if we should throw exceptions
                    if (ThrowLoopExceptions)
                        throw;

                    // Log the exception if we are not throwing it
                    Debug.WriteLine(anyException, "BluetoothLowEnergyLoopDeviceBase: Exception in device loop");
                }
                finally
                {
                    // Wait for a short period before the next iteration
                    await Task.Delay(25, cancellationToken);
                }
            }
        }


        public BluetoothLowEnergyLoopDeviceBase(string deviceNameRegex) : base(deviceNameRegex)
        {
            StartDeviceLoop(deviceLoopCancellationTokenSource.Token);
        }

        public BluetoothLowEnergyLoopDeviceBase(Guid serviceUUID) : base(serviceUUID)
        {
            StartDeviceLoop(deviceLoopCancellationTokenSource.Token);
        }
    }
}