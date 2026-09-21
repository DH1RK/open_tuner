# I2C-Analyse (Saleae Logic 2)

`i2c_analyze.py` wertet den CSV-Export eines Logic-2-I2C-Analyzers aus (Spalten Time, Packet ID, Address, Data, Read/Write, ACK/NAK) und zeigt
die Registerzugriffe des STV0910 mit Namen aus `MediaSources/Minitiouner/stv0910_regs.cs`.

Aufnahme: SDA und SCL des MiniTiouner-I2C-Busses, in Logic 2 einen I2C-Analyzer hinzufuegen, Daten als CSV exportieren.
Fuer eine lange Aufnahme reichen 4 bis 12 MS/s (I2C laeuft mit etwa 70 kHz); mit 125 MS/s wird die Datei sehr gross.

    python i2c_analyze.py capture.csv                      Ueberblick, erster Schreibzugriff je Register
    python i2c_analyze.py capture.csv --writes 17 19       alle Schreibzugriffe zwischen 17 und 19 s
    python i2c_analyze.py capture.csv --reads 0xF26B       Verlauf eines gelesenen Registers (hier TMGLOCK1)
    python i2c_analyze.py capture.csv --reads 0xF26B --from 28 --to 42

Die STV6120-Zugriffe (Adresse 0x60) und die LNA-Zugriffe (0x67 / 0x64) zaehlt der Ueberblick nach Bytes; die Register des STV6120 stehen in
`stv6120_regs.cs`. Die Auswertung der Aufnahme vom 21.09. (MiniTioune lockt 25 kS): `docs/MiniTioune_I2C_Analyse_25kS.md`.
