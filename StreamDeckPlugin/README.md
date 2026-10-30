# OpenTuner plugin for the Elgato Stream Deck

Controls OpenTuner from a Stream Deck: select a tuner, mute, show a tab, change the view, set the volume. The keys show the state of OpenTuner (tuner colour, lock, muted, selected tuner, active tab and view).

The plugin talks to OpenTuner through a small WebSocket server that only listens on this PC (`127.0.0.1`). The normal Stream Deck software stays in charge of the device, so other plugins and other Stream Decks keep working.

## Requirements

- Windows 10 or later
- Stream Deck software 7.1 or newer (the plugin uses version 3 of the Elgato SDK). Download: [elgato.com/downloads](https://www.elgato.com/downloads), the version is shown in the software under Preferences (About).
- OpenTuner (this fork) with the Stream Deck server switched on

The plugin comes ready to install in the OpenTuner release zip, you do not need Node.js or any other tool.

## 1. Switch the server on in OpenTuner

In OpenTuner open the **Main Settings**, tick **Stream Deck plugin (restart needed), port:** and enter a port (default **8765**, allowed 1024 to 65535). Save and restart OpenTuner.

The server is off by default and only accepts connections from this PC. If the port is used by another program the server does not start (OpenTuner keeps running, the reason is in the log, look for `Stream Deck server`).

## 2. Install the plugin

1. Unpack the OpenTuner release zip. In its main folder you find `com.dh1rk.opentuner.streamDeckPlugin`.
2. Make sure the Stream Deck software is running, then double click that file.
3. The Stream Deck software asks whether to install the plugin. Confirm with **Install**.
4. In the list of actions on the right you now find the category **OpenTuner**.

This has to be done **only once** per PC: the plugin stays in the Stream Deck software after a restart of the software or the PC. When a newer OpenTuner release brings a newer plugin file, double click that one the same way; it replaces the old one. The actions are dragged onto keys and dials once as well, the layout stays as long as you do not remove it.

## 3. Actions

Drag the actions from the category **OpenTuner** onto keys or dials. Every action has a Property Inspector with its settings. Where a tuner can be chosen, **Selected tuner** means the tuner selected in OpenTuner last (by a click on its video, a key or another action).

| Action | Key / dial | Settings | Display |
|---|---|---|---|
| **Select tuner** | Selects RX 1 to 4, like a click on its video | Tuner | Tuner colour, dimmed without lock, white frame on the selected tuner |
| **Mute tuner** | Switches the audio of a tuner on or off | Tuner or Selected tuner | Red and "Muted" while muted, otherwise "Audio" |
| **Mute all** | Mutes every tuner that is on; press again: only those tuners are switched on again | - | Green "Mute / all" on two rows, red "Muted / all" while all are muted (so it is not mistaken for the "Muted" of a single tuner) |
| **Tab** | Shows a tab of the board of the tuner: Properties ("Props" on the key), Expert, TS Info, Special, Chip | Tab, Tuner or Selected tuner | White frame while this tab is shown |
| **View** | Large (the others below), Only this stream, or the grid again | View, Tuner or Selected tuner | White frame while this view is active |
| **Volume** (dial) | Turn: volume, press: mute | Tuner or Selected tuner, step (1, 2, 5 or 10 %) | Tuner, percent and a bar |

The tuner colours are the ones of OpenTuner: RX 1 blue, RX 2 green, RX 3 orange, RX 4 violet. **Large** and **Only** select the tuner as well. Selecting a tuner also switches a tab you have open (Properties, Expert, ...) to the tab of that tuner's board, as a click in OpenTuner does.

The same "Mute all" is also available as the loudspeaker at the right end of the OpenTuner menu bar.

### Port

The port is a global setting: the same field in the Property Inspector of every action. If you change it in one, it changes for all. It must be the port set in OpenTuner (default 8765).

### Layout ideas

You lay the actions out yourself in the Stream Deck software, for example:

- **8x4 keys:** one column per tuner (Select, Mute, Large, Tab), "Mute all", Only and Grid on the right
- **4x3 keys with two dials:** the first row selects the tuner, the other rows act on the selected tuner (Mute, Large, Tab), the dials set the volume

## If it does not work

- The keys show **off**: the plugin does not reach OpenTuner. Is OpenTuner running with the server switched on (Main Settings) and the same port in the plugin? The plugin tries again every 3 seconds.
- **No tuner / "-"** on a key: the source in OpenTuner is not connected yet, or the chosen tuner number does not exist.
- The Property Inspector shows empty fields: it loads its controls (`sdpi-components`) from the internet, so it needs a connection.
- The plugin writes a log into the folder `logs` of its plugin folder under `%APPDATA%\Elgato\StreamDeck\Plugins` (when it stops at the start, the reason is there).
