import streamDeck from "@elgato/streamdeck";

import { allActions } from "./actions/actions.js";
import { client, DefaultPort } from "./client.js";

type GlobalSettings = { port?: string };

function portOf(settings: GlobalSettings): number {
	const port = Number(settings.port);
	return port >= 1024 && port <= 65535 ? port : DefaultPort;
}

for (const registered of allActions) {
	streamDeck.actions.registerAction(registered);
}

// every action draws itself again when the state of OpenTuner or the connection changed
client.onChange(() => allActions.forEach((registered) => registered.refreshAll()));

// the port is a global setting (the same field in the Property Inspector of every action)
streamDeck.settings.onDidReceiveGlobalSettings<GlobalSettings>((ev) => client.setPort(portOf(ev.settings)));

streamDeck.connect().then(async () => {
	client.setPort(portOf(await streamDeck.settings.getGlobalSettings<GlobalSettings>()));
});
