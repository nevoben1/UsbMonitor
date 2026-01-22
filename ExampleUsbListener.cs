using System;
using UsbMonitorLib;

namespace WindowsFormsApp1
{
    /// <summary>
    /// Example class demonstrating how ANY class in your application can listen for USB devices
    ///
    /// This class is completely independent from Form1, but both share the same
    /// UsbDeviceMonitor singleton instance. Each gets callbacks only for their
    /// registered VID/PID combinations.
    ///
    /// USAGE SCENARIO:
    /// - You might have a PrinterManager class listening for printer USB connections
    /// - A CameraManager class listening for camera USB connections
    /// - A StorageManager class listening for flash drive connections
    /// - All using the same UsbDeviceMonitor.Instance under the hood
    /// - No need to coordinate Start() calls - it auto-starts on first registration!
    /// </summary>
    public class ExampleUsbListener
    {
        private UsbDeviceRegistration myRegistration;

        /// <summary>
        /// Starts listening for a specific USB device
        /// </summary>
        public void StartListening()
        {
            // Get the singleton instance (same instance Form1 uses)
            var monitor = UsbDeviceMonitor.Instance;

            // Register for a specific device (example: VID:1234, PID:5678)
            // Replace with the actual VID/PID you want to monitor
            // The monitor will auto-start if this is the first registration
            myRegistration = monitor.Register(
                "1234_5678",                // VID_PID for your device
                OnMyDeviceConnected,        // Your connect callback
                OnMyDeviceDisconnected      // Your disconnect callback
            );

            // That's it! No need to call Start() - it happens automatically
            // You'll now get callbacks when device 1234_5678 connects/disconnects

            Console.WriteLine("ExampleUsbListener: Started listening for USB device 1234_5678");
        }

        /// <summary>
        /// Stops listening (cleanup)
        /// </summary>
        public void StopListening()
        {
            if (myRegistration != null)
            {
                var monitor = UsbDeviceMonitor.Instance;
                monitor.Unregister(myRegistration);
                myRegistration = null;

                Console.WriteLine("ExampleUsbListener: Stopped listening");
            }
        }

        /// <summary>
        /// Called when our specific device (VID:1234, PID:5678) is connected
        ///
        /// IMPORTANT: This is called from a background thread!
        /// If you need to update UI, use Invoke/BeginInvoke
        /// </summary>
        private void OnMyDeviceConnected(UsbDeviceEventArgs e)
        {
            // This callback is ONLY invoked when VID:1234 PID:5678 is connected
            // Other devices won't trigger this callback
            Console.WriteLine($"ExampleUsbListener: My device connected! VID:{e.VendorId} PID:{e.ProductId}");

            // Add your device-specific logic here
            // For example:
            // - Initialize communication with the device
            // - Open a serial port
            // - Start data transfer
            // - Update application state
        }

        /// <summary>
        /// Called when our specific device (VID:1234, PID:5678) is disconnected
        /// </summary>
        private void OnMyDeviceDisconnected(UsbDeviceEventArgs e)
        {
            // This callback is ONLY invoked when VID:1234 PID:5678 is disconnected
            Console.WriteLine($"ExampleUsbListener: My device disconnected! VID:{e.VendorId} PID:{e.ProductId}");

            // Add your cleanup logic here
            // For example:
            // - Close serial port
            // - Save state
            // - Show notification to user
            // - Attempt reconnection
        }
    }

    /// <summary>
    /// Another example class for a different device type
    /// This demonstrates that you can have as many listener classes as you need
    /// Each just calls Register() - no coordination needed!
    /// </summary>
    public class AnotherUsbListener
    {
        private UsbDeviceRegistration myRegistration;

        public void StartListening()
        {
            // This class listens for a DIFFERENT device (VID:ABCD, PID:EF01)
            // If this is called before any other Register(), it will auto-start the monitor
            // If called after another class already registered, it just adds to the existing monitor
            myRegistration = UsbDeviceMonitor.Instance.Register(
                "ABCD_EF01",
                (e) => Console.WriteLine($"AnotherUsbListener: Device ABCD_EF01 connected!"),
                (e) => Console.WriteLine($"AnotherUsbListener: Device ABCD_EF01 disconnected!")
            );

            // No need to call Start() - it's automatic!
        }

        public void StopListening()
        {
            if (myRegistration != null)
            {
                UsbDeviceMonitor.Instance.Unregister(myRegistration);
                myRegistration = null;
            }
        }
    }
}
