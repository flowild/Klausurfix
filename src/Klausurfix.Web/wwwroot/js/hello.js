import { api } from "./api.js";

function decode(value) {
  const base64 = value.replace(/-/g, "+").replace(/_/g, "/");
  const binary = atob(base64 + "=".repeat((4 - base64.length % 4) % 4));
  return Uint8Array.from(binary, c => c.charCodeAt(0));
}

function encode(value, url = true) {
  const bytes = new Uint8Array(value);
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  const base64 = btoa(binary);
  return url ? base64.replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "") : base64;
}

function creationOptions(options) {
  options.challenge = decode(options.challenge);
  options.user.id = decode(options.user.id);
  for (const item of options.excludeCredentials || []) item.id = decode(item.id);
  // The server model can serialize optional PRF fields as null, but WebAuthn
  // accepts an omitted field, not null, for the PRF input dictionaries.
  options.extensions = { prf: {} };
  return options;
}

function assertionOptions(options, salt) {
  options.challenge = decode(options.challenge);
  for (const item of options.allowCredentials || []) item.id = decode(item.id);
  options.extensions = { prf: { eval: { first: decode(salt) } } };
  return options;
}

function registrationJson(credential) {
  return {
    id: credential.id, rawId: encode(credential.rawId), type: credential.type,
    authenticatorAttachment: credential.authenticatorAttachment,
    response: {
      clientDataJSON: encode(credential.response.clientDataJSON),
      attestationObject: encode(credential.response.attestationObject),
      transports: credential.response.getTransports?.() || []
    },
    clientExtensionResults: {}
  };
}

function assertionJson(credential) {
  return {
    id: credential.id, rawId: encode(credential.rawId), type: credential.type,
    authenticatorAttachment: credential.authenticatorAttachment,
    response: {
      clientDataJSON: encode(credential.response.clientDataJSON),
      authenticatorData: encode(credential.response.authenticatorData),
      signature: encode(credential.response.signature),
      userHandle: credential.response.userHandle ? encode(credential.response.userHandle) : null
    },
    clientExtensionResults: {}
  };
}

async function getAssertion(options, salt) {
  const credential = await navigator.credentials.get({ publicKey: assertionOptions(options, salt) });
  const prf = credential?.getClientExtensionResults()?.prf?.results?.first;
  if (!prf || prf.byteLength !== 32) {
    throw new Error("Die Geräteanmeldung hat den benötigten Schlüssel nicht zurückgegeben. Bitte die App-PIN verwenden. Die Ursache kann an Windows, dem Browser oder dem gewählten Passkey-Anbieter liegen.");
  }
  return { credential: assertionJson(credential), prf: encode(prf, false) };
}

export async function registerHello(pin) {
  const start = await api.request("/api/auth/hello/register/options", { method: "POST", body: JSON.stringify({ pin }) });
  const credential = await navigator.credentials.create({ publicKey: creationOptions(start.options) });
  if (!credential || credential.authenticatorAttachment !== "platform") {
    throw new Error("Bitte die Anmeldung auf diesem Gerät auswählen.");
  }
  const confirmation = await api.request("/api/auth/hello/register/credential", {
    method: "POST", body: JSON.stringify({ token: start.token, credential: registrationJson(credential) })
  });
  const assertion = await getAssertion(confirmation.options, confirmation.salt);
  return api.request("/api/auth/hello/register/complete", {
    method: "POST", body: JSON.stringify({ token: confirmation.token, ...assertion })
  });
}

export async function unlockHello() {
  const start = await api.request("/api/auth/hello/unlock/options", { method: "POST" });
  const assertion = await getAssertion(start.options, start.salt);
  return api.request("/api/auth/hello/unlock/complete", {
    method: "POST", body: JSON.stringify({ token: start.token, ...assertion })
  });
}
