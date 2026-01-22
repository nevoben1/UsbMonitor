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
    /// Main form demonstrating USB device monitoring
    ///
    /// HOW TO USE THE USB MONITOR:
    /// 1. Create a UsbDeviceMonitor instance
    /// 2. Subscribe to DeviceConnected and DeviceRemoved events
    /// 3. Call Start() to begin monitoring
    /// 4. Handle events on the UI thread (use Invoke if needed)
    /// 5. Call Stop() and Dispose() when done
    /// </summary>
    public partial class Form1 : Form
    {
        // The USB monitor instance that detects device changes
        private UsbDeviceMonitor monitor;

        public Form1()
        {
            InitializeComponent();

            // Step 1: Create the monitor
            monitor = new UsbDeviceMonitor();

            // Step 2: Subscribe to events
            // These events fire when USB devices are plugged in or removed
            monitor.DeviceConnected += new EventHandler<UsbDeviceEventArgs>(monitor_DeviceConnected);
            monitor.DeviceRemoved += new EventHandler<UsbDeviceEventArgs>(monitor_DeviceRemoved);

            // Step 3: Start monitoring
            // From this point on, the monitor will notify us of any USB device changes
            monitor.Start();
        }

        /// <summary>
        /// Event handler called when a USB device is connected
        ///
        /// IMPORTANT: This event is fired from the monitor's background thread,
        /// not the UI thread! We must use Invoke to safely update the UI.
        /// </summary>
        private void monitor_DeviceConnected(object sender, UsbDeviceEventArgs e)
        {
            // Check if we need to marshal this call to the UI thread
            // InvokeRequired returns true if we're NOT on the UI thread
            if (this.InvokeRequired)
            {
                // Marshal the call to the UI thread and return
                this.Invoke(new EventHandler<UsbDeviceEventArgs>(monitor_DeviceConnected), sender, e);
                return;
            }

            // Now we're safely on the UI thread - show a message box
            MessageBox.Show(string.Format("USB CONNECTED : vid {0} , pid {1}", e.VendorId, e.ProductId));
        }

        /// <summary>
        /// Event handler called when a USB device is removed
        ///
        /// IMPORTANT: This event is fired from the monitor's background thread,
        /// not the UI thread! We must use Invoke to safely update the UI.
        /// </summary>
        private void monitor_DeviceRemoved(object sender, UsbDeviceEventArgs e)
        {
            // Check if we need to marshal this call to the UI thread
            if (this.InvokeRequired)
            {
                // Marshal the call to the UI thread and return
                this.Invoke(new EventHandler<UsbDeviceEventArgs>(monitor_DeviceRemoved), sender, e);
                return;
            }

            // Now we're safely on the UI thread - show a message box
            MessageBox.Show(string.Format("USB DISCONNECTED : vid {0} , pid {1}", e.VendorId, e.ProductId));
        }

        /// <summary>
        /// Clean up resources when the form is closing
        /// It's important to stop the monitor to properly clean up the background thread
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (monitor != null)
            {
                // Stop the monitoring thread
                monitor.Stop();

                // Release resources
                monitor.Dispose();
            }
            base.OnFormClosing(e);
        }
    }
}
