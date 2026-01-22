using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using UsbMonitorLib;

namespace WindowsFormsApp1
{
    /// <summary>
    /// Main form demonstrating USB device monitoring with the singleton pattern
    ///
    /// HOW TO USE THE USB MONITOR (SIMPLE!):
    /// 1. Get the singleton instance: UsbDeviceMonitor.Instance
    /// 2. Register with your VID/PID and callbacks - that's it!
    /// 3. The monitor auto-starts on first registration
    /// 4. Handle callbacks on the UI thread (use Invoke if needed)
    /// 5. Unregister when done (optional)
    ///
    /// HOW TO FIND YOUR USB DEVICE VID/PID:
    /// 1. Plug in your USB device
    /// 2. Open Device Manager (Windows key + X, then M)
    /// 3. Find your device in the list
    /// 4. Right-click -> Properties -> Details tab
    /// 5. Select "Hardware Ids" from the dropdown
    /// 6. Look for "USB\VID_XXXX&PID_YYYY" in the value
    /// 7. Use "VID_XXXX&PID_YYYY" as your registration string
    ///
    /// MULTIPLE LISTENERS EXAMPLE:
    /// - Class A registers for VID_046D&PID_C52B (Logitech device)
    /// - Class B registers for VID_0781&PID_5567 (SanDisk flash drive)
    /// - Both share the same UsbDeviceMonitor.Instance
    /// - Each gets callbacks only for their specific device
    /// - No need to coordinate Start() calls - it's automatic!
    /// </summary>
    public partial class Form1 : Form
    {
        // Registration objects for each device we're monitoring
        private UsbDeviceRegistration registration1;
        private UsbDeviceRegistration registration2;

        public Form1()
        {
            InitializeComponent();

            // Get the singleton instance
            var monitor = UsbDeviceMonitor.Instance;

            // Example 1: Register for a Logitech device (VID_046D&PID_C52B)
            // Replace "VID_046D&PID_C52B" with your actual device's VID/PID from Device Manager
            // The monitor will auto-start when you call Register()
            registration1 = monitor.Register(
                "VID_046D&PID_C52B",            // Windows device path format (case insensitive)
                OnLogitechDeviceConnected,      // Called when this specific device connects
                OnLogitechDeviceDisconnected    // Called when this specific device disconnects
            );

            // Example 2: Register for a different device (VID_0781&PID_5567)
            // This demonstrates multiple registrations in the same class
            // Since the monitor is already started from registration1, this just adds another listener
            registration2 = monitor.Register(
                "VID_0781&PID_5567",            // Different device pattern
                OnSandiskDeviceConnected,       // Different callbacks for this device
                OnSandiskDeviceDisconnected
            );

            // That's it! No need to call Start() - it happens automatically
            // You'll now get callbacks when these specific devices are connected/disconnected

            System.Diagnostics.Debug.WriteLine("Form1 registered for USB device notifications");
        }

        /// <summary>
        /// Callback for Logitech device connection
        ///
        /// IMPORTANT: This is called from the monitor's background thread!
        /// Use Invoke to safely update the UI.
        /// </summary>
        private void OnLogitechDeviceConnected(UsbDeviceEventArgs e)
        {
            // Marshal to UI thread if needed
            if (this.InvokeRequired)
            {
                this.Invoke(new UsbDeviceConnectedCallback(OnLogitechDeviceConnected), e);
                return;
            }

            // Now on UI thread - safe to show MessageBox
            MessageBox.Show(string.Format("LOGITECH DEVICE CONNECTED!\nVID: {0}\nPID: {1}\nPath: {2}",
                e.VendorId, e.ProductId, e.DevicePath));
        }

        /// <summary>
        /// Callback for Logitech device disconnection
        /// </summary>
        private void OnLogitechDeviceDisconnected(UsbDeviceEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new UsbDeviceDisconnectedCallback(OnLogitechDeviceDisconnected), e);
                return;
            }

            MessageBox.Show(string.Format("LOGITECH DEVICE DISCONNECTED!\nVID: {0}\nPID: {1}",
                e.VendorId, e.ProductId));
        }

        /// <summary>
        /// Callback for SanDisk device connection
        /// </summary>
        private void OnSandiskDeviceConnected(UsbDeviceEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new UsbDeviceConnectedCallback(OnSandiskDeviceConnected), e);
                return;
            }

            MessageBox.Show(string.Format("SANDISK DEVICE CONNECTED!\nVID: {0}\nPID: {1}\nPath: {2}",
                e.VendorId, e.ProductId, e.DevicePath));
        }

        /// <summary>
        /// Callback for SanDisk device disconnection
        /// </summary>
        private void OnSandiskDeviceDisconnected(UsbDeviceEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new UsbDeviceDisconnectedCallback(OnSandiskDeviceDisconnected), e);
                return;
            }

            MessageBox.Show(string.Format("SANDISK DEVICE DISCONNECTED!\nVID: {0}\nPID: {1}",
                e.VendorId, e.ProductId));
        }

        /// <summary>
        /// Clean up resources when the form is closing
        /// Unregister our listeners (optional but good practice)
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            var monitor = UsbDeviceMonitor.Instance;

            // Unregister our listeners
            if (registration1 != null)
            {
                monitor.Unregister(registration1);
                registration1 = null;
            }

            if (registration2 != null)
            {
                monitor.Unregister(registration2);
                registration2 = null;
            }

            // Note: We don't call Stop() or Dispose() on the singleton
            // Other parts of the application might still be using it
            // The singleton will clean up when the application exits

            base.OnFormClosing(e);
        }
    }
}
