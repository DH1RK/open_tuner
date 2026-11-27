
// mostly ported from longmynd - https://github.com/myorangedragon/longmynd - Heather Lomond

using System;
using System.Collections.Generic;
using FTD2XX_NET;
using System.Threading;
using LibUsbDotNet;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;
using System.Collections.ObjectModel;
using opentuner.MediaSources.Minitiouner.HardwareInterfaces;
using Serilog;

namespace opentuner
{
    public class PicoTunerInterface : MTHardwareInterface
    {
        const int USB_TIMEOUT = 5000;

        // LibUsbDotNet 3.x: device discovery/opening goes through a UsbContext instance instead
        // of the old static UsbDevice.AllDevices/UsbRegistry API - must stay alive for as long as
        // the devices it opened are in use, disposed together with them in hw_close().
        // All of this belongs to one PicoTuner: several of them can be in use at the same time (#272).
        UsbContext usb_context;

        IUsbDevice i2c_pt_device;
        IUsbDevice ts_pt_device;

        UsbEndpointWriter i2cEndPointWriter = null;
        UsbEndpointReader i2cEndPointReader = null;
        UsbEndpointReader ts2EndPointReader = null;
        UsbEndpointReader ts1EndPointReader = null;

        public const byte TS1 = 0;
        public const byte TS2 = 1;

        // Clock
        const uint ClockDivisor = 0x0095;

        // Sending and receiving
        uint NumBytesToSend = 0;
        int NumBytesSent = 0;
        int NumBytesRead = 0;
        byte[] MPSSEbuffer = new byte[500];
        byte[] InputBuffer = new byte[500];
        byte[] InputBuffer2 = new byte[500];
        byte I2C_Status = 0;
        public bool Running = true;

        // we only have 1 port on picotuner for control (old FTDI AC Port)

        // high byte
        /* Default GPIO value 0x6f = 0b01101111 = LNB Bias Off, LNB Voltage 12V, NIM not reset */
        byte gpio_value = 0x01;
      
        // all pins on picotuner set as output
        byte gpio_direction = 0xFF;

        /*
        // low byte
        byte ftdi_gpio_lowbyte_value = 0x00;
        byte ftdi_gpio_lowbyte_direction = 0xFF;

        // high byte pins
        const byte FTDI_GPIO_PINID_NIM_RESET = 0;
        const byte FTDI_GPIO_PINID_TS2SYNC = 1;
        const byte FTDI_GPIO_PINID_LNB_BIAS_ENABLE = 4;
        const byte FTDI_GPIO_PINID_LED1 = 5;
        const byte FTDI_GPIO_PINID_LED2 = 6;
        const byte FTDI_GPIO_PINID_LNB_BIAS_VSEL = 7;

        const byte FTDI_GPIO_PINID_LNB2_BIAS_ENABLE = 4;
        const byte FTDI_GPIO_PINID_LNB2_BIAS_VSEL = 5;
        */

        /*
            #define AC0 2         //NIM RESET LOW
            #define AC1 3         //
            #define AC2 4         //J8-6
            #define AC3 5         //J8-5
            #define AC4 6         //J8-4  LNB Bias Enable 
            #define AC5 7         //J8-3 
            #define AC6 8         //J8-2 
            #define AC7 9         //J8-1  LNB BIAS Voltage Select

        */

        const byte GPIO_NIM_RESET = 0;

        const byte GPIO_LNB1_ENABLE = 4;
        const byte GPIO_LNB2_ENABLE = 5;
        const byte GPIO_LNB2_VSEL = 6;
        const byte GPIO_LNB1_VSEL = 7;

        public override bool RequireSerialTS => true;

        public override string GetName => "PicoTuner";

        private byte Receive_Data_i2c(uint BytesToRead)
        {
            uint QueueTimeOut = 0;
            uint Buffer1Index = 0;
            uint Buffer2Index = 0;
            int TotalBytesRead = 0;
            bool QueueTimeoutFlag = false;
            int NumBytesRxd = 0;

            if (i2cEndPointReader == null)
            {
                Log.Information("error: i2cEndPointReader is null");
                return 1;
            }

            // Keep looping until all requested bytes are received or we've tried 5000 times (value can be chosen as required)
            while ((TotalBytesRead < BytesToRead) && (QueueTimeoutFlag == false))
            {
                //ftStatus = ftdiDevice_i2c.GetRxBytesAvailable(ref NumBytesInQueue);       // Check bytes available


                var error = i2cEndPointReader.Read(InputBuffer, USB_TIMEOUT, out NumBytesRxd);

                //ftStatus = ftdiDevice_i2c.Read(InputBuffer, NumBytesInQueue, ref NumBytesRxd);  // if any available read them

                if (NumBytesRxd < 3)
                {
                    Log.Information("Empty Response");
                }

                if (error == Error.Success)
                {
                    // first two bytes are ftdi bytes
                    Buffer1Index = 2;

                    while (Buffer1Index < NumBytesRxd)
                    {
                        InputBuffer2[Buffer2Index] = InputBuffer[Buffer1Index];     // copy into main overall application buffer
                        Buffer1Index++;
                        Buffer2Index++;
                    }
                    TotalBytesRead = TotalBytesRead + NumBytesRxd;                  // Keep track of total
                }
                else
                {
                    Log.Information("EndPointError: " + error.ToString());
                    return 1;
                }

                QueueTimeOut++;
                if (QueueTimeOut == 5000)
                    QueueTimeoutFlag = true;
                else
                    Thread.Sleep(0);                                                // Avoids running Queue status checks back to back
            }
            // returning globals NumBytesRead and the buffer InputBuffer2
            NumBytesRead = TotalBytesRead;

            if (QueueTimeoutFlag == true)
            {
                Log.Information("Queue Timout Error");
                return 1;
            }
            else
            {
                //Log.Information("Read: " + (NumBytesRead - 2).ToString());
                return 0;
            }
        }

        private byte Send_Data_i2c(uint BytesToSend)
        {
            if (i2cEndPointWriter == null)
            {
                Log.Information("Error: Endpointwriter is null");
                return 1;
            }

            NumBytesToSend = BytesToSend;

            var error = i2cEndPointWriter.Write(MPSSEbuffer, 0, (int)BytesToSend, USB_TIMEOUT, out NumBytesSent);

            // Ensure that call completed OK and that all bytes sent as requested
            if ((NumBytesSent != NumBytesToSend) || error != Error.Success)
            {
                Log.Information("Error: " + error.ToString());
                Log.Information("Send: " + NumBytesToSend.ToString());
                Log.Information("Sent: " + NumBytesSent.ToString());
                return 1;   // error   calling function can check NumBytesSent to see how many got sent
            }
            else
                return 0;   // success
        }

        private byte FlushBuffer(FTD2XX_NET.FTDI ftdi)
        {
            byte err = 0;

            Log.Information("Flush Buffer Requested");
            i2cEndPointReader.ReadFlush();

            return err;
        }


        // ***********************************************
        private byte ftdi_set_mpsse_mode(FTD2XX_NET.FTDI ftdi)
        {
            Log.Information("Flow: FTDI set mpsse mode");

            return 0;
        }

        private byte ftdi_set_ftdi_io(FTD2XX_NET.FTDI ftdi)
        {
            /***** Flush the buffer *****/
            I2C_Status = FlushBuffer(ftdi);

            Log.Information(i2c_pt_device.IsOpen.ToString());
            Log.Information(i2cEndPointReader.ToString());
            Log.Information(i2cEndPointWriter.ToString());

            /***** Synchronize the MPSSE interface by sending bad command 0xAA *****/
            NumBytesToSend = 0;
            MPSSEbuffer[NumBytesToSend++] = 0xAA;
            I2C_Status = Send_Data_i2c(NumBytesToSend);
            if (I2C_Status != 0)
                return 1; // error();
            I2C_Status = Receive_Data_i2c(2);
            if (I2C_Status != 0)
                return 1; //error();

            if ((InputBuffer2[0] == 0xFA) && (InputBuffer2[1] == 0xAA))
            {
                Log.Information("mppse synced");
            }
            else
            {
                return 1;            //error();
            }


            NumBytesToSend = 0;

            MPSSEbuffer[NumBytesToSend++] = 0x8A; 	// Disable clock divide by 5 for 60Mhz master clock
            MPSSEbuffer[NumBytesToSend++] = 0x97;	// Disable adaptive clocking
            MPSSEbuffer[NumBytesToSend++] = 0x8D;   // Disable 3 phase data clocking

            I2C_Status = Send_Data_i2c(NumBytesToSend);
            if (I2C_Status != 0)
            {
                return 1;            //error();
            }

            NumBytesToSend = 0;

            MPSSEbuffer[NumBytesToSend++] = 0x80; // set ouput, low byte
            MPSSEbuffer[NumBytesToSend++] = 0x00; // value
            MPSSEbuffer[NumBytesToSend++] = 0xFF; // direction

            MPSSEbuffer[NumBytesToSend++] = 0x82; // set output, high byte
            MPSSEbuffer[NumBytesToSend++] = gpio_value; // value
            MPSSEbuffer[NumBytesToSend++] = gpio_direction; // direction

            MPSSEbuffer[NumBytesToSend++] = 0x86; 	//Command to set clock divisor
            MPSSEbuffer[NumBytesToSend++] = (byte)(ClockDivisor & 0x00FF);	//Set 0xValueL of clock divisor
            MPSSEbuffer[NumBytesToSend++] = (byte)((ClockDivisor >> 8) & 0x00FF);	//Set 0xValueH of clock divisor

            I2C_Status = Send_Data_i2c(NumBytesToSend);
            if (I2C_Status != 0)
            {
                return 1;            //error();
            }

            Thread.Sleep(30);

            NumBytesToSend = 0;

            MPSSEbuffer[NumBytesToSend++] = 0x85; 			// loopback off
            I2C_Status = Send_Data_i2c(NumBytesToSend);
            NumBytesToSend = 0;

            if (I2C_Status != 0)
            {
                return 1;            //error();
            }

            Thread.Sleep(30);

            return 0;
        }

        byte ftdi_nim_reset()
        {
            Log.Information("Nim Reset");

            byte err = gpio_write(0, false);
            Thread.Sleep(10);

            if (err != 0)
                return 1;

            gpio_write(0, true);
            Thread.Sleep(10);

            return err;
        }

        byte ftdi_i2c_set_start()
        {
            int count;

            // FTDI_STOP_START_REPEATS = 4

            for (count = 0; count < 4; count++)
            {
                MPSSEbuffer[NumBytesToSend++] = 0x80;
                MPSSEbuffer[NumBytesToSend++] = 0x03;
                MPSSEbuffer[NumBytesToSend++] = 0x13;
            }

            for (count = 0; count < 4; count++)
            {
                MPSSEbuffer[NumBytesToSend++] = 0x80;
                MPSSEbuffer[NumBytesToSend++] = 0x01;
                MPSSEbuffer[NumBytesToSend++] = 0x13;
            }

            return 0;
        }

        byte ftdi_i2c_set_stop()
        {
            int count;

            // FTDI_STOP_START_REPEATS = 4

            for (count = 0; count < 4; count++)
            {
                MPSSEbuffer[NumBytesToSend++] = 0x80;
                MPSSEbuffer[NumBytesToSend++] = 0x01;
                MPSSEbuffer[NumBytesToSend++] = 0x13;
            }

            for (count = 0; count < 4; count++)
            {
                MPSSEbuffer[NumBytesToSend++] = 0x80;
                MPSSEbuffer[NumBytesToSend++] = 0x03;
                MPSSEbuffer[NumBytesToSend++] = 0x13;
            }

            MPSSEbuffer[NumBytesToSend++] = 0x80;
            MPSSEbuffer[NumBytesToSend++] = 0x03;
            MPSSEbuffer[NumBytesToSend++] = 0x10;

            return 0;
        }


        private byte ftdi_i2c_send_byte_check_ack(byte b)
        {
            byte err;

            MPSSEbuffer[NumBytesToSend++] = 0x80; // low byte
            MPSSEbuffer[NumBytesToSend++] = 0x00; // value
            MPSSEbuffer[NumBytesToSend++] = 0x13; // direction

            MPSSEbuffer[NumBytesToSend++] = 0x11; // clock data bytes out
            MPSSEbuffer[NumBytesToSend++] = 0x00; // length l
            MPSSEbuffer[NumBytesToSend++] = 0x00; // length h
            MPSSEbuffer[NumBytesToSend++] = b;    // byte

            MPSSEbuffer[NumBytesToSend++] = 0x80; // low byte
            MPSSEbuffer[NumBytesToSend++] = 0x00; // value
            MPSSEbuffer[NumBytesToSend++] = 0x11; // direction

            MPSSEbuffer[NumBytesToSend++] = 0x27; // ?
            MPSSEbuffer[NumBytesToSend++] = 0x00; // ?
            MPSSEbuffer[NumBytesToSend++] = 0x87; // ?

            err = Send_Data_i2c(NumBytesToSend);
            NumBytesToSend = 0;

            if (err == 0)
                err = Receive_Data_i2c(1);

            int i = NumBytesRead;

            if (err == 0 && (InputBuffer2[0] & 0x01) != 0)
            {
                err = 18;
            }

            return err;
        }

        private byte ftdi_i2c_read_byte_send_nak(ref byte b)
        {
            byte err;

            MPSSEbuffer[NumBytesToSend++] = 0x80;
            MPSSEbuffer[NumBytesToSend++] = 0x00;
            MPSSEbuffer[NumBytesToSend++] = 0x13;

            MPSSEbuffer[NumBytesToSend++] = 0x80;
            MPSSEbuffer[NumBytesToSend++] = 0x00;
            MPSSEbuffer[NumBytesToSend++] = 0x11;

            MPSSEbuffer[NumBytesToSend++] = 0x25; // ?
            MPSSEbuffer[NumBytesToSend++] = 0x00; // ?
            MPSSEbuffer[NumBytesToSend++] = 0x00; // ?
            MPSSEbuffer[NumBytesToSend++] = 0x87; // ?


            err = Send_Data_i2c(NumBytesToSend);
            NumBytesToSend = 0;

            if (err == 0)
                err = Receive_Data_i2c(1);

            b = InputBuffer2[0];

            return err;

        }


        private byte ftdi_i2c_output()
        {
            byte err;

            err = Send_Data_i2c(NumBytesToSend);
            NumBytesToSend = 0;

            return err;
        }

        public override byte nim_read_reg8(byte addr, byte reg, ref byte val)
        {
            byte err = 0;
            int i = 0;
            int timeout = 0;

            do
            {
                for (i = 0; i < 10; i++)
                {
                    err = ftdi_i2c_set_start();
                    err |= ftdi_i2c_send_byte_check_ack(addr);
                    err |= ftdi_i2c_send_byte_check_ack(reg);
                    err |= ftdi_i2c_set_stop();
                    err |= ftdi_i2c_output();
                    if (err == 0) break;
                }

                if (err == 0)
                {
                    for (i = 0; i < 10; i++)
                    {
                        err = ftdi_i2c_set_start();
                        err |= ftdi_i2c_send_byte_check_ack((byte)(addr | 0x01));
                        err |= ftdi_i2c_read_byte_send_nak(ref val);
                        err |= ftdi_i2c_set_stop();
                        err |= ftdi_i2c_output();
                        if (err == 0) break;
                    }
                }


            } while (err != 0 && (timeout != 100));

            return err;
        }

        public override byte nim_write_reg8(byte addr, byte reg, byte val)
        {
            byte err = 0;
            int i;
            int timeout = 0;

            do
            {
                for ( i = 0; i < 10; i++ )
                {
                    err = ftdi_i2c_set_start();
                    err|= ftdi_i2c_send_byte_check_ack(addr);
                    err |= ftdi_i2c_send_byte_check_ack(reg);
                    err |= ftdi_i2c_send_byte_check_ack(val);
                    err |= ftdi_i2c_set_stop();
                    err |= ftdi_i2c_output();

                    if (err == 0) break;
                }

            } while ((err != 0) && timeout != 100);

            return err;
        }

        public override byte nim_write_reg16(byte addr, ushort reg, byte val)
        {

            byte err = 0;
            short i;
            short timeout = 0;

            do
            {
                for (i = 0; i < 10; i++)
                {
                    err = ftdi_i2c_set_start();
                    err |= ftdi_i2c_send_byte_check_ack(addr);
                    err |= ftdi_i2c_send_byte_check_ack((byte)(reg >> 8));
                    err |= ftdi_i2c_send_byte_check_ack((byte)(reg & 0xFF));
                    err |= ftdi_i2c_send_byte_check_ack(val);
                    err |= ftdi_i2c_set_stop();
                    err |= ftdi_i2c_output();

                    if (err == 0)
                        break;
                }

                timeout += 1;
            } while (err != 0 && timeout != 100);

            if (err != 0)
            {
                //MessageBox.Show("Error Write Reg 16");
            }

            return err;
        }

        public override byte nim_read_reg16(byte addr, ushort reg, ref byte val)
        {

            byte err = 0;
            int i = 0;
            int timeout = 0;

            do
            {
                for (i = 0; i < 10; i++)
                {
                    err = ftdi_i2c_set_start();
                    err |= ftdi_i2c_send_byte_check_ack(addr);
                    err |= ftdi_i2c_send_byte_check_ack((byte)(reg >> 8));
                    err |= ftdi_i2c_send_byte_check_ack((byte)(reg & 0xff));
                    if (err == 0)
                        break;
                }

                if (err == 0)
                {
                    for (i = 0; i < 10; i++)
                    {
                        err = ftdi_i2c_set_start();
                        err |= ftdi_i2c_send_byte_check_ack((byte)(addr | 0x01));
                        err |= ftdi_i2c_read_byte_send_nak(ref val);
                        err |= ftdi_i2c_set_stop();
                        err |= ftdi_i2c_output();
                        if (err == 0)
                            break;
                    }
                }

                timeout += 1;

            } while (err != 0 && timeout != 100);

            if (err != 0)
            {
                //MessageBox.Show("Error Read Reg 16");
            }

            return err;
        }

        // Generic raw I2C write - see FTDIInterface.i2c_write_raw for details, identical
        // implementation since this class uses the same bit-banged I2C primitives.
        public override byte i2c_write_raw(byte addr, byte[] data)
        {
            byte err = 0;
            byte write_addr = (byte)(addr << 1);

            for (int attempt = 0; attempt < I2cWriteRawAttempts; attempt++)
            {
                if (attempt > 0)
                    Thread.Sleep(I2cWriteRawRetryPauseMs);

                err = ftdi_i2c_set_start();
                err |= ftdi_i2c_send_byte_check_ack(write_addr);

                for (int d = 0; d < data.Length && err == 0; d++)
                {
                    err |= ftdi_i2c_send_byte_check_ack(data[d]);
                }

                err |= ftdi_i2c_set_stop();
                err |= ftdi_i2c_output();

                if (err == 0) break;
            }

            return err;
        }

        // get a list of all detected ft2232 devices
        public List<FTDIDevice> detect_all_ftdi()
        {
            List<FTDIDevice> ftdi_devices = new List<FTDIDevice>();
            /*

            try
            {
                ftStatus = ftdiDevice_i2c.GetNumberOfDevices(ref device_count);
                Log.Information("Number of FTDI Devices: " + device_count.ToString());

                for (uint c = 0; c < device_count; c++)
                {
                    FTDI ftdi_device = new FTDI();
                    ftdi_device.OpenByIndex(c);

                    FTDI.FT_DEVICE device = new FTDI.FT_DEVICE();
                    ftdi_device.GetDeviceType(ref device);

                    // is this a ft2232 device?
                    if (device.ToString() != "FT_DEVICE_2232H")
                    {
                        ftdi_device.Close();
                        continue;
                    }

                    FTDIDevice detected_ftdi_device = new FTDIDevice();
                    
                    // device index
                    detected_ftdi_device.device_index = c;

                    // device serial number
                    ftdi_device.GetSerialNumber(out string SerialNumber);
                    detected_ftdi_device.device_serial_number = SerialNumber;

                    // lets get description
                    ftdi_device.GetDescription(out string DeviceName);
                    detected_ftdi_device.device_description = DeviceName;

                    ftdi_device.Close();

                    ftdi_devices.Add(detected_ftdi_device);
                }
            }
            catch (Exception ex)
            {
                Log.Information("FTDI Detect Error: " + ex.Message);
            }
            */

            return ftdi_devices;
        }

        public override byte hw_detect(ref uint i2c_port, ref uint ts_port, ref uint ts_port2, ref uint aux_port, ref string detectedDeviceName, string i2c_serial, string ts_serial, string ts2_serial, string aux_serial)
        {
            byte err = 0;

            i2c_port = 0;
            ts_port = 0;
            ts_port2 = 0;
            aux_port = 99; // PicoTuner has no second (EXTERN-0..7) chip
            detectedDeviceName = "PicoTuner";

            return err;
        }

        public override byte hw_detect(ref uint i2c_port, ref uint ts_port, ref uint ts_port2, ref uint aux_port, ref string detectedDeviceName)
        {

            byte err = 0;

            i2c_port = 0;
            ts_port = 0;
            ts_port2 = 0;
            aux_port = 99; // PicoTuner has no second (EXTERN-0..7) chip
            detectedDeviceName = "PicoTuner";

            return err;
        }

        public override bool AuxAvailable => false;

        public override byte aux_gpio_write(byte value)
        {
            return 1; // not supported on PicoTuner
        }

        // ---- Finding and choosing the PicoTuner (#231) ----------------------------------------------------------------
        // The BATC PicoTuner is ONE libusb device (RP2040) with two interfaces (0 = i2c, 1 = TS). The product string
        // "BATC PicoTuner" is empty before Open() and has no "i2c"/"TS" suffix: those are only the Windows interface
        // names. The USB firmware reports no serial number, so several PicoTuners can only be told apart by the USB
        // port they are plugged into.
        const ushort PICOTUNER_VID = 0x2E8A;
        const ushort PICOTUNER_PID = 0xBA2C;

        // setting "PreferredPicoTunerPort" ("" = the first free one), set before hw_init
        public string PreferredPort { get; set; } = "";

        // true: only the PicoTuner on PreferredPort, no other one (a further board of a source with several boards, #272)
        public bool PreferredPortOnly { get; set; } = false;

        // what hw_init found out about the PicoTuner in use, "" before
        public string Port { get; private set; } = "";
        public string FirmwareVersion { get; private set; } = "";
        public string TsMode { get; private set; } = "";

        // The USB port as text: bus number, then the port numbers from the root hub to the device, e.g. "USB 1-4.2"
        private static string PortOf(IUsbDevice device)
        {
            if (!(device is UsbDevice usb_device))
                return "USB ?";

            return "USB " + usb_device.BusNumber + "-" + string.Join(".", usb_device.PortNumbers);
        }

        // bcdDevice of the firmware: 0x0101 parallel TS, 0x0102 serial TS
        private static string TsModeText(ushort release)
        {
            switch (release)
            {
                case 0x0101: return "parallel";
                case 0x0102: return "serial";
                default: return "unknown (device release 0x" + release.ToString("X4") + ")";
            }
        }

        // 1st choice: the known VID/PID (nothing is opened). Only without such a device: any other RP2040 device
        // (VID 2E8A) whose product string, readable after Open(), contains "PicoTuner". The result is not open.
        private static List<IUsbDevice> FindPicoTuners(IEnumerable<IUsbDevice> devices)
        {
            var found = new List<IUsbDevice>();

            foreach (IUsbDevice device in devices)
            {
                if (device.VendorId == PICOTUNER_VID && device.ProductId == PICOTUNER_PID)
                    found.Add(device);
            }

            if (found.Count > 0)
                return found;

            foreach (IUsbDevice device in devices)
            {
                if (device.VendorId != PICOTUNER_VID)
                    continue;

                try
                {
                    device.Open();
                    string product = device.Info.Product ?? "";
                    device.Close();
                    Log.Information("PicoTuner: RP2040 device PID {Pid:X4} product '{Product}'", device.ProductId, product);

                    if (product.Contains("PicoTuner"))
                        found.Add(device);
                }
                catch (Exception ex)
                {
                    Log.Warning("PicoTuner: RP2040 device PID {Pid:X4} not readable ({Message})", device.ProductId, ex.Message);
                }
            }

            return found;
        }

        // opens the device and claims both interfaces; false (and closed again) when that is not possible
        private static bool TryClaim(IUsbDevice device)
        {
            try
            {
                device.Open();

                // interface 0 = BATC PicoTuner i2c, interface 1 = BATC PicoTuner TS
                if (device.ClaimInterface(0) && device.ClaimInterface(1))
                    return true;

                Log.Warning("PicoTuner at {Port}: could not claim the interfaces (in use by another program?)", PortOf(device));
            }
            catch (Exception ex)
            {
                Log.Warning("PicoTuner at {Port}: could not be opened, in use by another program? ({Message})", PortOf(device), ex.Message);
            }

            try { device.Close(); } catch (Exception) { }
            return false;
        }

        // All PicoTuners on USB, sorted by port. own_ports: the ports of the connected ones (they are reported as "open"
        // and not touched), null = none. check_state false: nothing is opened, the state stays empty.
        public static List<PicoTunerDevice> ListDevices(ICollection<string> own_ports, bool check_state, out string error)
        {
            var list = new List<PicoTunerDevice>();
            error = null;

            try
            {
                using (var context = new UsbContext())
                using (var devices = context.List())
                {
                    foreach (IUsbDevice device in FindPicoTuners(devices))
                    {
                        var entry = new PicoTunerDevice { Port = PortOf(device), Product = "BATC PicoTuner", State = "" };

                        if (own_ports != null && own_ports.Contains(entry.Port))
                        {
                            entry.State = "open";
                        }
                        else if (check_state)
                        {
                            if (TryClaim(device))
                            {
                                entry.Product = device.Info.Product ?? entry.Product;
                                entry.State = "free";
                                device.Close();
                            }
                            else
                            {
                                entry.State = "in use";
                            }
                        }

                        list.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                // e.g. libusb-1.0.dll missing
                Log.Warning(ex, "PicoTuner: USB device list failed");
                error = ex.Message;
            }

            list.Sort((a, b) => string.CompareOrdinal(a.Port, b.Port));
            return list;
        }

        // the firmware answers command 0xBA with its version (major, minor); older firmware answers "bad command" (0xFA, 0xBA)
        private void ReadFirmwareVersion()
        {
            NumBytesToSend = 0;
            MPSSEbuffer[NumBytesToSend++] = 0xBA;
            byte err = Send_Data_i2c(NumBytesToSend);
            NumBytesToSend = 0;

            if (err == 0)
                err = Receive_Data_i2c(2);

            if (err != 0)
                FirmwareVersion = "not readable";
            else if (InputBuffer2[0] == 0xFA && InputBuffer2[1] == 0xBA)
                FirmwareVersion = "not reported (older firmware)";
            else
                FirmwareVersion = InputBuffer2[0] + "." + InputBuffer2[1].ToString("00");

            Log.Information("PicoTuner: firmware version {Version}", FirmwareVersion);
        }

        public override byte hw_init(uint i2c_device, uint ts_device, uint ts_device2, uint aux_device)
        {
            byte err = 0;
            usb_context = new UsbContext();

            // Not disposing this UsbDeviceCollection - i2c_pt_device/ts_pt_device below keep
            // direct references into it for the lifetime of the connection, closed explicitly
            // (alongside usb_context itself) in hw_close().
            var allDevices = usb_context.List();

            // what libusb sees, to find out why a PicoTuner is not found (#231)
            Log.Information("PicoTuner: libusb lists {Count} USB devices", allDevices.Count);
            foreach (IUsbDevice usbDevice in allDevices)
                Log.Information("PicoTuner: USB device VID {Vid:X4} PID {Pid:X4} at {Port}", usbDevice.VendorId, usbDevice.ProductId, PortOf(usbDevice));

            List<IUsbDevice> candidates = FindPicoTuners(allDevices);

            if (candidates.Count == 0)
            {
                Log.Error("PicoTuner not found (no libusb device VID 2E8A PID BA2C and no RP2040 device with product 'PicoTuner')");
                hw_close();
                return 1;
            }

            // by USB port, so that "the first free one" is the same one every time
            candidates.Sort((a, b) => string.CompareOrdinal(PortOf(a), PortOf(b)));

            if (PreferredPortOnly)
            {
                candidates.RemoveAll(d => PortOf(d) != PreferredPort);
                if (candidates.Count == 0)
                {
                    Log.Error("PicoTuner: no PicoTuner on USB port {Port}", PreferredPort);
                    hw_close();
                    return 1;
                }
            }
            // the PicoTuner on the selected USB port first, the others stay behind it
            else if (!string.IsNullOrEmpty(PreferredPort))
            {
                int preferred = candidates.FindIndex(d => PortOf(d) == PreferredPort);
                if (preferred < 0)
                {
                    Log.Warning("PicoTuner: no PicoTuner on the selected USB port {Port}, using the first free one", PreferredPort);
                }
                else
                {
                    var selected = candidates[preferred];
                    candidates.RemoveAt(preferred);
                    candidates.Insert(0, selected);
                }
            }

            // the first one that can be opened: another one may be in use (a second OpenTuner instance)
            IUsbDevice pico_device = null;
            foreach (IUsbDevice candidate in candidates)
            {
                if (TryClaim(candidate))
                {
                    pico_device = candidate;
                    break;
                }
            }

            if (pico_device == null)
            {
                Log.Error("PicoTuner: {Count} found, none could be opened (in use by another program?)", candidates.Count);
                hw_close();
                return 1;
            }

            Port = PortOf(pico_device);
            TsMode = TsModeText(pico_device.Info.Device);
            Log.Information("PicoTuner: using device VID {Vid:X4} PID {Pid:X4} product '{Product}' manufacturer '{Manufacturer}' at {Port}, TS {TsMode}",
                pico_device.VendorId, pico_device.ProductId, pico_device.Info.Product, pico_device.Info.Manufacturer, Port, TsMode);

            // Both variables refer to the same physical USB device.
            i2c_pt_device = pico_device;
            ts_pt_device = pico_device;

            i2cEndPointWriter = i2c_pt_device.OpenEndpointWriter(WriteEndpointID.Ep02);
            if (i2cEndPointWriter == null)
            {
                Log.Error("PicoTuner i2c WriteEndpoint not found");
                hw_close();
                return 1;
            }
            Log.Information("I2C Endpoint Writer : " + i2cEndPointWriter.EndpointInfo.ToString());//EndpointAddress.ToString("X"));

            i2cEndPointReader = i2c_pt_device.OpenEndpointReader(ReadEndpointID.Ep01);
            if (i2cEndPointReader == null)
            {
                Log.Error("PicoTuner i2c ReadEndpoint not found");
                hw_close();
                return 1;
            }
            Log.Information("I2C Endpoint Reader : " + i2cEndPointReader.EndpointInfo.ToString());//     EndpointAddress.ToString("X"));

            ts2EndPointReader = ts_pt_device.OpenEndpointReader(ReadEndpointID.Ep03);
            if (ts2EndPointReader == null)
            {
                Log.Error("PicoTuner TS2 ReadEndpoint not found");
                hw_close();
                return 1;
            }
            Log.Information("TS2 Endpoint Reader : " + ts2EndPointReader.EndpointInfo.ToString());//EndpointAddress.ToString("X"));

            ts1EndPointReader = ts_pt_device.OpenEndpointReader(ReadEndpointID.Ep04);
            if (ts1EndPointReader == null)
            {
                Log.Error("PicoTuner TS1 ReadEndpoint not found");
                hw_close();
                return 1;
            }
            Log.Information("TS1 Endpoint Reader : " + ts1EndPointReader.EndpointInfo.ToString());//EndpointAddress.ToString("X"));

            err = ftdi_set_mpsse_mode(null);
            if (err == 0) err = ftdi_set_ftdi_io(null);
            if (err == 0) ReadFirmwareVersion();   // only information, never stops the connect
            if (err == 0) err = ftdi_nim_reset();

            return err;
        }

        public override void hw_close()
        {
            // UsbEndpointReader/Writer are no longer individually IDisposable in LibUsbDotNet 3.x -
            // they're owned by their parent device and cleaned up when it closes.
            // i2c_pt_device and ts_pt_device are the same device: close it only once.
            i2c_pt_device?.Close();
            i2c_pt_device = null;
            ts_pt_device = null;
            Port = "";

            // Free usb resources.
            // This is necessary for libusb-1.0 and Linux compatibility.
            usb_context?.Dispose();
            usb_context = null;
        }

        byte gpio_write(byte pin_id, bool pin_value)
        {
            Log.Information("Flow: GPIO Write: pin {0} -> value {1}", pin_id, pin_value);

            //Log.Information("ftdi_gpio_highbyte_value: before: " + Convert.ToString(gpio_value, 2).PadLeft(8,'0'));

            if (pin_value)
            {
                gpio_value |= (byte)(1 << pin_id);
            }
            else
            {
                gpio_value &= (byte)(~(1 << pin_id));
            }


            NumBytesToSend = 0;
            MPSSEbuffer[NumBytesToSend++] = 0x82; /* aka. MPSSE_CMD_SET_DATA_BITS_HIGHBYTE */
            MPSSEbuffer[NumBytesToSend++] = gpio_value;
            MPSSEbuffer[NumBytesToSend++] = gpio_direction;

            Log.Information(Convert.ToString(gpio_value, 2).PadLeft(8, '0'));
            Log.Information("0x82: [" + gpio_value + "][" + gpio_direction + "]");

            I2C_Status = Send_Data_i2c(NumBytesToSend);

            Log.Information("Result:" + I2C_Status.ToString());

            NumBytesToSend = 0;

            return I2C_Status;
        }

        public override byte transport_read(int device, ref byte[] data, ref uint bytesRead)
        {
            byte[] readdata = new byte[4096];

            int iBytesRead = 0;

            Error error;

            if (device == TS2)
            {
                error = ts2EndPointReader.Read(readdata, USB_TIMEOUT, out iBytesRead);
            }
            else
            {
                error = ts1EndPointReader.Read(readdata, USB_TIMEOUT, out iBytesRead);
            }

            if (error != Error.Success)
            {
                Log.Information("TS Read Error" + error.ToString());
                return 1;
            }

            // empty response
            if (iBytesRead == 2)
            {
                bytesRead = 0;
                return 0;
            }

            // we receive the data in chunks of 512, first two bytes of each chunk needs to be removed
            if (iBytesRead % 512 != 0)
            {
                Log.Information("Ignoring: " + iBytesRead + "," + (iBytesRead % 512).ToString());
                bytesRead = 0;
                return 0;
            }

            int chunks = iBytesRead / 512;

            for ( int c = 0; c < chunks; c++ )
                Array.Copy(readdata, (c * 512) + 2, data, c * 510, 510);

            bytesRead = (uint)(chunks * 510);

            return 0;
        }

        public override byte transport_flush(int device)
        {           
            byte err = 0;

            if (device == TS2)
                ts2EndPointReader.ReadFlush();
            else
                ts1EndPointReader.ReadFlush();

            return err;
        }


        public override byte hw_ts_led(int led, bool setting)
        {
            /*
            byte err = 0;

            switch (led)
            {
                case 0: 
                    ftdi_gpio_write_highbyte(FTDI_GPIO_PINID_LED1, !setting);
                    break;
                case 1:
                    ftdi_gpio_write_highbyte(FTDI_GPIO_PINID_LED2, !setting);
                    break;
            }


            return err;

            */

            return 0;
        }

        // on minitiouner pro 2 there are 2 outputs for the 2 different lnb switching - longmynd originally only catered for 1 output, the pro 2 needs two outputs. 
        // need to confirm express and S versions.


        // lnb_num 0 = LNB-A, 1 = LNB-B of the program. LNB-A feeds RF IN_2 (the GPIO_LNB2 pins), LNB-B feeds RF IN_1
        // (the GPIO_LNB1 pins), as on the MiniTiouner (#165).
        public override byte hw_set_polarization_supply(byte lnb_num, bool supply_enable, bool supply_horizontal)
        {
            byte err = 0;

            byte enable_pin = lnb_num == 0 ? GPIO_LNB2_ENABLE : GPIO_LNB1_ENABLE;
            byte vsel_pin = lnb_num == 0 ? GPIO_LNB2_VSEL : GPIO_LNB1_VSEL;
            string name = lnb_num == 0 ? "LNB-A (RF IN_2)" : "LNB-B (RF IN_1)";

            Log.Information("PicoTuner at {Port}: {Lnb} supply {State}", Port, name,
                supply_enable ? (supply_horizontal ? "on, 18 V (horizontal)" : "on, 13 V (vertical)") : "off");

            if (supply_enable)
            {
                // the voltage first, then the power
                gpio_write(vsel_pin, supply_horizontal);
                gpio_write(enable_pin, true);
            }
            else
            {
                gpio_write(enable_pin, false);
                gpio_write(vsel_pin, false);
            }
            return err;
        }
    }

    // one PicoTuner on USB, as listed for Hardware Info and the source settings
    public class PicoTunerDevice
    {
        public string Port { get; set; }       // "USB 1-4.2"
        public string Product { get; set; }
        public string State { get; set; }      // "open", "free", "in use" or "" (not checked)
    }

    public class FTDIDevice
    {
        public uint device_index { get; set; }
        public string device_serial_number { get; set; }
        public string device_description { get; set; }
    }
}
