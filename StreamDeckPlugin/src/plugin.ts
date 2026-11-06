import streamDeck from "@elgato/streamdeck";

import { allActions } from "./actions/actions.js";
import { onAnyChange } from "./client.js";

for (const registered of allActions) {
	streamDeck.actions.registerAction(registered);
}

// every action draws itself again when the state or the connection of an OpenTuner instance changed
// (the port is a setting of each key, the connections are made by the actions)
onAnyChange(() => allActions.forEach((registered) => registered.refreshAll()));

streamDeck.connect();
