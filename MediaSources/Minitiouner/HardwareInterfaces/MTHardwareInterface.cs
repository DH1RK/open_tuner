using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace opentuner.MediaSources.Minitiouner.HardwareInterfaces
{
    public abstract class MTHardwareInterface
    {
        public MTHardwareInterface() { }

        public abstract bool RequireSerialTS { get;  }

        public abstract string GetName { get; }

        // TODO: change to more generic detect
        // aux_port: the MiniTiounerPro's second FT2232H chip ("MiniTiouner_Pro_TS1 A" - I2C-EXT
        // bus + EXTERN-0..7 GPIO, see MiniTiounerPro V2 schematic sheet 5). Left at 99 (unset) on
        // boards/hardware interfaces that don't have this second chip.
        public abstract byte hw_detect(ref uint i2c_port, ref uint ts_port, ref uint ts_port2, ref uint aux_port, ref string detectedDeviceName, string i2c_serial, string ts_serial, string ts2_serial, string aux_serial);
        public abstract byte hw_detect(ref uint i2c_port, ref uint ts_port, ref uint ts_port2, ref uint aux_port, ref string detectedDeviceName);

        // TODO: change to more generic init
        // aux_device: 99 = not present/not opened (see hw_detect's aux_port)
        public abstract byte hw_init(uint i2c_device, uint ts_device, uint ts_device2, uint aux_device);

        // True once hw_init has successfully opened the AUX chip (aux_device != 99).
        public abstract bool AuxAvailable { get; }

        // Writes the full EXTERN-0..7 state (one bit per output) to the AUX chip's GPIO high
        // byte. No-op (returns error) if AuxAvailable is false. Safe to call from any thread -
        // talks to a physically separate FTDI device/USB connection from the NIM I2C bus, with
        // its own buffers, so it can't race with nim_write_*/i2c_write_raw.
        public abstract byte aux_gpio_write(byte value);

        // Direct, individual pin test/debug write for the LNB EN/SEL lines - bypasses
        // hw_set_polarization_supply entirely, to isolate whether each bit toggles independently.
        // Uses ftdiDevice_i2c (the MASTER chip's low/high GPIO byte), so - unlike aux_gpio_write -
        // this MUST be called from NimThread's own worker thread (see NimThread.SetTestGpio),
        // never directly from the UI thread, to avoid racing nim_write_*/i2c_write_raw on the
        // shared static MPSSEbuffer.
        // AD6_FORCE_HIGH/AD7_FORCE_HIGH: debug-only - temporarily forces that pin to output+high
        // (checked) or back to its normal input mode (unchecked), to test whether the LNB1/LNB2
        // indicator LED circuit responds differently than with AD6/AD7 left as inputs.
        public enum TestGpioPin { EN_LNB1, SEL_LNB1, EN_LNB2, SEL_LNB2, AD6_FORCE_HIGH, AD7_FORCE_HIGH }
        public abstract byte hw_gpio_write_test(TestGpioPin pin, bool value);

        public abstract void hw_close();

        public abstract byte hw_set_polarization_supply(byte lnb_num, bool supply_enable, bool supply_horizontal);
        public abstract byte hw_ts_led(int led, bool setting);


        public abstract byte transport_flush(int device);
        public abstract byte transport_read(int device, ref byte[] data, ref uint bytesRead);

        public abstract byte nim_read_reg8(byte addr, byte reg, ref byte val);
        public abstract byte nim_write_reg8(byte addr, byte reg, byte val);
        public abstract byte nim_write_reg16(byte addr, ushort reg, byte val);
        public abstract byte nim_read_reg16(byte addr, ushort reg, ref byte val);

        // Generic raw I2C write for accessory devices that share the NIM I2C bus (no
        // register concept, unlike the nim_* calls above) - e.g. a Digole status display
        // wired to JP3/"I2C-NIM" on a MiniTiounerPro V2 board. addr is the 7-bit I2C
        // address; data is written as one continuous I2C transaction (single START..STOP).
        public abstract byte i2c_write_raw(byte addr, byte[] data);
    }
}
