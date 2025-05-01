//! src/message.rs
use serde::{Deserialize, Serialize};
use serde_json;

/// The wire format you defined: { "type": 1, "target": "...", "arguments": [...] }
#[derive(Debug, Deserialize)]
pub struct Incoming {
    #[serde(rename = "type")]
    pub frame_type: u8, // always 1 for invocation frames
    pub target: String,
    pub arguments: Vec<serde_json::Value>,
}

/// Canonical outgoing format (handy helper)
#[derive(Debug, Serialize)]
pub struct Outgoing<'a, T: Serialize> {
    #[serde(rename = "type")]
    pub frame_type: u8, // 1 for invocation, 2 for streamItem, etc.  Keep 1 for now
    pub target: &'a str,
    pub arguments: [T; 1], // we usually send a single payload object
}

/// Convenience constructor
impl<'a, T: Serialize> Outgoing<'a, T> {
    pub fn new(target: &'a str, payload: T) -> Self {
        Self {
            frame_type: 1,
            target,
            arguments: [payload],
        }
    }
}

/// Strongly-typed list of *known* client → server events.
/// (Add more as you implement.)
#[derive(Debug)]
pub enum ClientEvent {
    Join { name: String },
    Ready(bool),
    Chat { text: String },
    RawUnknown, // fallback for unrecognised targets
}

/// Parse `Incoming` into typed `ClientEvent`
pub fn to_client_event(msg: Incoming) -> Result<ClientEvent, String> {
    match msg.target.as_str() {
        "join" => {
            let payload = msg.arguments.get(0).ok_or("join expects 1 argument")?;
            #[derive(Deserialize)]
            struct JoinPayload {
                name: String,
            }
            let JoinPayload { name } = serde_json::from_value(payload.clone())
                .map_err(|e| format!("bad join payload: {e}"))?;
            Ok(ClientEvent::Join { name })
        }
        "ready" => {
            let flag = msg
                .arguments
                .get(0)
                .and_then(|v| v.as_bool())
                .ok_or("ready expects a bool")?;
            Ok(ClientEvent::Ready(flag))
        }
        "chat" => {
            let payload = msg.arguments.get(0).ok_or("chat expects 1 argument")?;
            #[derive(Deserialize)]
            struct ChatPayload {
                text: String,
            }
            let ChatPayload { text } = serde_json::from_value(payload.clone())
                .map_err(|e| format!("bad chat payload: {e}"))?;
            Ok(ClientEvent::Chat { text })
        }
        _ => Ok(ClientEvent::RawUnknown),
    }
}

// ─────────────────────────────────────────────────────────
// unit tests for message router
// These are compiled only when `cargo test` is run.
#[cfg(test)]
mod tests {
    // Bring the parent module’s items into scope
    use super::*;
    use serde_json::json;

    #[test]
    fn join_event_parses() {
        // JSON identical to what the client sends
        let frame = json!({
            "type": 1,
            "target": "join",
            "arguments": [ { "name": "Effy" } ]
        });

        // ➊ deserialize to `Incoming`
        let inc: Incoming = serde_json::from_value(frame).unwrap();

        // ➋ convert to typed event
        let evt = crate::message::to_client_event(inc).unwrap();

        // ➌ assert the enum variant and its data
        assert!(matches!(evt, ClientEvent::Join { name } if name == "Effy"));
    }

    #[test]
    fn ready_event_parses() {
        let frame = json!({
            "type": 1,
            "target": "ready",
            "arguments": [ true ]
        });

        let inc: Incoming = serde_json::from_value(frame).unwrap();
        let evt = crate::message::to_client_event(inc).unwrap();

        assert!(matches!(evt, ClientEvent::Ready(true)));
    }

    #[test]
    fn unknown_target_is_raw_unknown() {
        let frame = json!({
            "type": 1,
            "target": "foo",
            "arguments": []
        });

        let inc: Incoming = serde_json::from_value(frame).unwrap();
        let evt = crate::message::to_client_event(inc).unwrap();

        assert!(matches!(evt, ClientEvent::RawUnknown));
    }
}
