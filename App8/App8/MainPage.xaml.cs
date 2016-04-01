// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.ObjectModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Storage.Streams;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maker.Firmata;
using Microsoft.Maker.RemoteWiring;
using Microsoft.Maker.Serial;

namespace App8
{
    public sealed partial class MainPage : Page
    {
        /// <summary>
        /// Private variables
        /// </summary>
        private SerialDevice serialPort = null;
        DataWriter dataWriteObject = null;
        DataReader dataReaderObject = null;

        private ObservableCollection<DeviceInformation> listOfDevices;
        private CancellationTokenSource ReadCancellationTokenSource;

        double x_orientation = 0.0;
        double y_orientation = 0.0;
        double z_orientation = 0.0;

        double x_init = 0.0;
        double y_init = 0.0;
        double z_init = 0.0;
        Boolean calibrated = false;
        Boolean reversing = false;

        public MainPage()
        {
            this.InitializeComponent();
            this.InitArduino(); //Init Arduino connection  
            comPortInput.IsEnabled = false;
            sendTextButton.IsEnabled = false;
            listOfDevices = new ObservableCollection<DeviceInformation>();
            ListAvailablePorts();
        }

        Microsoft.Maker.RemoteWiring.RemoteDevice arduino;
        Microsoft.Maker.Serial.NetworkSerial netWorkSerial;

        public void InitArduino()
        {
            //Establish a network serial connection. change it to the right IP address and port
            netWorkSerial = new Microsoft.Maker.Serial.NetworkSerial(new Windows.Networking.HostName("192.168.0.106"), 3030);

            //Create Arduino Device
            arduino = new Microsoft.Maker.RemoteWiring.RemoteDevice(netWorkSerial);

            //Attach event handlers
            netWorkSerial.ConnectionEstablished += NetWorkSerial_ConnectionEstablished;
            netWorkSerial.ConnectionFailed += NetWorkSerial_ConnectionFailed;

            //Begin connection
            netWorkSerial.begin(115200, Microsoft.Maker.Serial.SerialConfig.SERIAL_8N1);
        }

        private void NetWorkSerial_ConnectionEstablished()
        {
            arduino.pinMode(1, Microsoft.Maker.RemoteWiring.PinMode.OUTPUT); //Set the pin to output
            arduino.pinMode(2, Microsoft.Maker.RemoteWiring.PinMode.OUTPUT); //Set the pin to output
            arduino.pinMode(6, Microsoft.Maker.RemoteWiring.PinMode.OUTPUT);
            arduino.pinMode(3, Microsoft.Maker.RemoteWiring.PinMode.OUTPUT);
        }

        private void NetWorkSerial_ConnectionFailed(string message)
        {
            System.Diagnostics.Debug.WriteLine("Arduino Connection Failed: " + message);
        }
        /// <summary>
        /// ListAvailablePorts
        /// - Use SerialDevice.GetDeviceSelector to enumerate all serial devices
        /// - Attaches the DeviceInformation to the ListBox source so that DeviceIds are displayed
        /// </summary>
        private async void ListAvailablePorts()
        {
            try
            {
                string aqs = SerialDevice.GetDeviceSelector();
                var dis = await DeviceInformation.FindAllAsync(aqs);

                status.Text = "Select a device and connect";

                for (int i = 0; i < dis.Count; i++)
                {
                    listOfDevices.Add(dis[i]);
                }

                DeviceListSource.Source = listOfDevices;
                comPortInput.IsEnabled = true;
                ConnectDevices.SelectedIndex = -1;
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
        }

        /// <summary>
        /// comPortInput_Click: Action to take when 'Connect' button is clicked
        /// - Get the selected device index and use Id to create the SerialDevice object
        /// - Configure default settings for the serial port
        /// - Create the ReadCancellationTokenSource token
        /// - Start listening on the serial port input
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void comPortInput_Click(object sender, RoutedEventArgs e)
        {
            var selection = ConnectDevices.SelectedItems;

            if (selection.Count <= 0)
            {
                status.Text = "Select a device and connect";
                return;
            }

            DeviceInformation entry = (DeviceInformation)selection[0];

            try
            {
                serialPort = await SerialDevice.FromIdAsync(entry.Id);

                // Disable the 'Connect' button 
                comPortInput.IsEnabled = false;

                // Configure serial settings
                serialPort.WriteTimeout = TimeSpan.FromMilliseconds(1000);
                serialPort.ReadTimeout = TimeSpan.FromMilliseconds(1000);
                serialPort.BaudRate = 9600;
                serialPort.Parity = SerialParity.None;
                serialPort.StopBits = SerialStopBitCount.One;
                serialPort.DataBits = 8;
                serialPort.Handshake = SerialHandshake.None;

                // Display configured settings
                status.Text = "Serial port configured successfully: ";
                status.Text += serialPort.BaudRate + "-";
                status.Text += serialPort.DataBits + "-";
                status.Text += serialPort.Parity.ToString() + "-";
                status.Text += serialPort.StopBits;

                // Set the RcvdText field to invoke the TextChanged callback
                // The callback launches an async Read task to wait for data
                rcvdText.Text = "Waiting for data...";

                // Create cancellation token object to close I/O operations when closing the device
                ReadCancellationTokenSource = new CancellationTokenSource();

                // Enable 'WRITE' button to allow sending data
                sendTextButton.IsEnabled = true;

                Listen();

            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                comPortInput.IsEnabled = true;
                sendTextButton.IsEnabled = false;
            }
        }

        /// <summary>
        /// sendTextButton_Click: Action to take when 'WRITE' button is clicked
        /// - Create a DataWriter object with the OutputStream of the SerialDevice
        /// - Create an async task that performs the write operation
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void sendTextButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (serialPort != null)
                {
                    // Create the DataWriter object and attach to OutputStream
                    dataWriteObject = new DataWriter(serialPort.OutputStream);

                    //Launch the WriteAsync task to perform the write
                    await WriteAsync();
                }
                else
                {
                    status.Text = "Select a device and connect";
                }
            }
            catch (Exception ex)
            {
                status.Text = "sendTextButton_Click: " + ex.Message;
            }
            finally
            {
                // Cleanup once complete
                if (dataWriteObject != null)
                {
                    dataWriteObject.DetachStream();
                    dataWriteObject = null;
                }
            }
        }

        /// <summary>
        /// WriteAsync: Task that asynchronously writes data from the input text box 'sendText' to the OutputStream 
        /// </summary>
        /// <returns></returns>
        private async Task WriteAsync()
        {
            Task<UInt32> storeAsyncTask;

            if (sendText.Text.Length != 0)
            {
                // Load the text from the sendText input text box to the dataWriter object
                dataWriteObject.WriteString(sendText.Text);

                // Launch an async task to complete the write operation
                storeAsyncTask = dataWriteObject.StoreAsync().AsTask();

                UInt32 bytesWritten = await storeAsyncTask;
                if (bytesWritten > 0)
                {
                    status.Text = sendText.Text + ", ";
                    status.Text += "bytes written successfully!";
                }
                sendText.Text = "";
            }
            else
            {
                status.Text = "Enter the text you want to write and then click on 'WRITE'";
            }
        }

        /// <summary>
        /// - Create a DataReader object
        /// - Create an async task to read from the SerialDevice InputStream
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Listen()
        {
            try
            {
                if (serialPort != null)
                {
                    dataReaderObject = new DataReader(serialPort.InputStream);

                    // keep reading the serial input
                    while (true)
                    {
                        await ReadAsync(ReadCancellationTokenSource.Token);
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.GetType().Name == "TaskCanceledException")
                {
                    status.Text = "Reading task was cancelled, closing device and cleaning up";
                    CloseDevice();
                }
                else
                {
                    status.Text = ex.Message;
                }
            }
            finally
            {
                // Cleanup once complete
                if (dataReaderObject != null)
                {
                    dataReaderObject.DetachStream();
                    dataReaderObject = null;
                }
            }
        }

        private void start()
        {
            if(reversing == true)
            {
                stop();
            }
            arduino.digitalWrite(1, Microsoft.Maker.RemoteWiring.PinState.LOW);
            arduino.digitalWrite(6, Microsoft.Maker.RemoteWiring.PinState.LOW);
            arduino.analogWrite(2, 255);
            arduino.analogWrite(3, 255);
            reversing = false;
        }

        private void stop()
        {
            arduino.analogWrite(2, 0);
            arduino.analogWrite(3, 0);
        }

        private void go_left()
        {
            //arduino.digitalWrite(1, Microsoft.Maker.RemoteWiring.PinState.LOW);
            //arduino.digitalWrite(6, Microsoft.Maker.RemoteWiring.PinState.LOW);
            arduino.analogWrite(2, 255);
            arduino.analogWrite(3, 0);
        }

        private void go_right()
        {
            //arduino.digitalWrite(1, Microsoft.Maker.RemoteWiring.PinState.LOW);
            //arduino.digitalWrite(6, Microsoft.Maker.RemoteWiring.PinState.LOW);
            arduino.analogWrite(2, 0);
            arduino.analogWrite(3, 255);
        }

        private void reverse()
        {
            if (reversing == false)
            {
                stop();
            }
            arduino.digitalWrite(1, Microsoft.Maker.RemoteWiring.PinState.HIGH);
            arduino.digitalWrite(6, Microsoft.Maker.RemoteWiring.PinState.HIGH);
            arduino.analogWrite(2, 255);
            arduino.analogWrite(3, 255);
            reversing = true;
        }
        /// <summary>
        /// ReadAsync: Task that waits on data and reads asynchronously from the serial device InputStream
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task ReadAsync(CancellationToken cancellationToken)
        {
            Task<UInt32> loadAsyncTask;

            uint ReadBufferLength = 1024;

            // If task cancellation was requested, comply
            cancellationToken.ThrowIfCancellationRequested();

            // Set InputStreamOptions to complete the asynchronous read operation when one or more bytes is available
            dataReaderObject.InputStreamOptions = InputStreamOptions.Partial;

            // Create a task object to wait for data on the serialPort.InputStream
            loadAsyncTask = dataReaderObject.LoadAsync(ReadBufferLength).AsTask(cancellationToken);

            // Launch the task and wait       
            UInt32 bytesRead = await loadAsyncTask;
            string sensor_raw;
            char[] delimiterChars = { '\t', '\n' };
            char[] colon = { ':' };
            string[] sensor_parts;
            string[] x_parts;
            string[] y_parts;
            string[] z_parts;
            double x_orientation_new = 0.0;
            double y_orientation_new = 0.0;
            double z_orientation_new = 0.0;
            double x_corrected = 0.0;
            double result = -1;

            if (bytesRead > 0)
            {
                sensor_raw = dataReaderObject.ReadString(bytesRead);

                rcvdText.Text = sensor_raw;
                status.Text = "bytes read successfully!";

                sensor_parts = sensor_raw.Split(delimiterChars);

                for (int i = 0; i < sensor_parts.Length - 1; i++) //go backward so you get most updated info
                {
                    if (sensor_parts[i].StartsWith("X"))
                    {
                        x_parts = sensor_parts[i].Split(colon);
                        Double.TryParse(x_parts[1].Trim(), out result);
                        if (result != 0)
                        {
                            x_orientation_new = result;
                            sensor_part.Text = x_parts[1];
                        }

                        //calibrate
                        if ((Math.Abs(x_orientation_new - x_orientation) >= 20) && calibrated == false)
                        {
                            y_init = y_orientation;
                            x_init = x_orientation;
                            calibrated = true;
                        }

                        //check if going right over 360 border
                        if (x_orientation_new <= 360 && x_orientation_new >= 270 && x_init <= 90 && x_init >= 0 && calibrated == true)
                        {
                            x_corrected = x_orientation_new - 360;
                            /*if ((x_corrected + x_init) >= 75)
                            {
                                go_right();
                            }*/
                        }
                        //check if going left over 360 border
                        else if (x_orientation_new <= 90 && x_orientation_new >= 0 && x_init <= 360 && x_init >= 270 && calibrated == true)
                        {
                            x_corrected = x_orientation_new + 360;
                            /*if (Math.Abs(x_corrected - x_init) >= 75)
                            {
                                go_left();
                            }*/
                        }
                        //for all other cases
                        else if (calibrated == true)
                        {
                            x_corrected = x_orientation_new;
                        }

                        if (Math.Abs(x_corrected - x_init) >= 75 && calibrated == true)
                        {
                            if (x_corrected > x_init)
                            {
                                go_left();
                            }
                            else if (x_corrected < x_init)
                            {
                                go_right();
                            }
                        }
                        else if (Math.Abs(x_corrected - x_init) <= 35 && Math.Abs(y_orientation_new - y_init) >= 50 && Math.Abs(y_orientation_new - y_init) <= 60 && calibrated == true)
                        {
                            start();
                        }
                        else if (Math.Abs(x_corrected - x_init) <= 35 && Math.Abs(y_orientation_new - y_init) >= 90 && calibrated == true)
                        {
                            reverse();
                        }
                    }
                    else if (sensor_parts[i].StartsWith("Y"))
                    {
                        y_parts = sensor_parts[i].Split(colon);
                        Double.TryParse(y_parts[1].Trim(), out result);
                        if (result != 0)
                        {
                            y_orientation_new = result;
                            sensor_part.Text = y_parts[1];
                        }

                        if ((Math.Abs(y_orientation_new - y_orientation) >= 50) && calibrated == false)
                        {
                            y_init = y_orientation;
                            x_init = x_orientation;
                            calibrated = true;
                        }


                        if ((Math.Abs(y_orientation_new - y_init) <= 40) && calibrated == true)
                        {
                            stop();
                        }

                        /*if ((Math.Abs(y_orientation_new - y_orientation) >= 50) && calibrated == true)
                        {
                            start();
                        }*/
                            
                    }
                    /*else if (sensor_parts[i].StartsWith("Z"))
                    {
                        z_parts = sensor_parts[i].Split(colon);
                        Double.TryParse(z_parts[1].Trim(), out result);
                        if (result != 0)
                        {
                            z_orientation_new = result;
                            sensor_part.Text = z_parts[1];
                        }

                        if ((Math.Abs(z_orientation_new - z_orientation) >= 40) && calibrated == false)
                        {
                            z_init = z_orientation;
                            y_init = y_orientation;
                            calibrated = true;
                        }
                        if ((Math.Abs(z_orientation_new - z_init) >= 40) && calibrated == true)
                        {
                            if (z_orientation_new < 0)
                            {
                                go_right();
                            }
                            else if (z_orientation_new > 0)
                            {
                                go_left();
                            }
                        }
                    }*/
                    /*else if (sensor_parts[i].StartsWith("A"))
                    {
                        ax_parts = sensor_parts[i].Split(colon);
                        Double.TryParse(ax_parts[1].Trim(), out result);
                        if (result != 0)
                        {
                            x_acceleration_new = result;
                            sensor_part.Text = ax_parts[1];
                        }
                        if (Math.Abs(x_acceleration_new - x_acceleration) >= 35)
                        {
                            if (z_orientation_new < 0)
                            {
                                stop();
                            }
                            else if (z_orientation_new > 0)
                            {
                                start();
                            }
                        }
                    }*/
                }
                x_orientation = x_orientation_new;
                y_orientation = y_orientation_new;
                z_orientation = z_orientation_new;
                bytes_read.Text = Convert.ToString(bytesRead);
            }
        }

        /// <summary>
        /// CancelReadTask:
        /// - Uses the ReadCancellationTokenSource to cancel read operations
        /// </summary>
        private void CancelReadTask()
        {
            if (ReadCancellationTokenSource != null)
            {
                if (!ReadCancellationTokenSource.IsCancellationRequested)
                {
                    ReadCancellationTokenSource.Cancel();
                }
            }
        }

        /// <summary>
        /// CloseDevice:
        /// - Disposes SerialDevice object
        /// - Clears the enumerated device Id list
        /// </summary>
        private void CloseDevice()
        {
            if (serialPort != null)
            {
                serialPort.Dispose();
            }
            serialPort = null;

            comPortInput.IsEnabled = true;
            sendTextButton.IsEnabled = false;
            rcvdText.Text = "";
            listOfDevices.Clear();
        }

        /// <summary>
        /// closeDevice_Click: Action to take when 'Disconnect and Refresh List' is clicked on
        /// - Cancel all read operations
        /// - Close and dispose the SerialDevice object
        /// - Enumerate connected devices
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void closeDevice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                status.Text = "";
                CancelReadTask();
                CloseDevice();
                ListAvailablePorts();
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
        }

        private void Calibrate_Click(object sender, RoutedEventArgs e)
        {
            calibrated = false;
            y_init = 0.0;
            z_init = 0.0;
        }
    }
}
