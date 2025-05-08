use serde::Deserialize;
use serde_json;

#[derive(Debug, Deserialize)]
pub struct Incoming {
    #[serde(rename = "type")]
    pub frame_type: u8,
    pub target: String,
    pub arguments: Vec<serde_json::Value>,
}

#[derive(Debug)]
pub enum ClientEvent {
    Join {
        name: String,
    },
    Ready(bool),
    Chat {
        text: String,
    },
    NightAction {
        action: String,
        target: String,
    },
    Vote {
        target: String,
    },
    RegisterPublicKey {
        public_key: String,
    },
    ShuffleDone {
        encrypted_deck: Vec<[String; 2]>,
        public_inputs: Vec<String>,
        proof: String,
    },
    PickCard {
        card: usize,
    },
    RawUnknown,
}

pub fn to_client_event(msg: Incoming) -> Result<ClientEvent, String> {
    let _frame_type = msg.frame_type;
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
            #[derive(Deserialize)]
            struct ChatPayload {
                text: String,
            }
            let payload = msg.arguments.get(0).ok_or("chat expects 1 argument")?;
            let ChatPayload { text } = serde_json::from_value(payload.clone())
                .map_err(|e| format!("bad chat payload: {e}"))?;
            Ok(ClientEvent::Chat { text })
        }
        "pickCard" => {
            let idx = msg
                .arguments
                .get(0)
                .and_then(|v| v.get("card"))
                .and_then(|v| v.as_u64())
                .ok_or("pickCard expects {card:uint}")? as usize;
            Ok(ClientEvent::PickCard { card: idx })
        }
        "nightAction" => {
            let payload = msg
                .arguments
                .get(0)
                .ok_or("nightAction expects 1 argument")?;
            #[derive(Deserialize)]
            struct NightPayload {
                action: String,
                target: String,
            }
            let NightPayload { action, target } = serde_json::from_value(payload.clone())
                .map_err(|e| format!("bad nightAction payload: {e}"))?;
            Ok(ClientEvent::NightAction { action, target })
        }
        "vote" => {
            let tgt = msg
                .arguments
                .get(0)
                .and_then(|v| v.as_str())
                .ok_or("vote expects a string target")?;
            Ok(ClientEvent::Vote {
                target: tgt.to_string(),
            })
        }
        "registerPublicKey" => {
            let pk = msg
                .arguments
                .get(0)
                .and_then(|v| v.as_str())
                .ok_or("registerPublicKey expects a string public key")?;
            Ok(ClientEvent::RegisterPublicKey {
                public_key: pk.to_string(),
            })
        }
        "shuffleDone" => {
            // arguments[0] is expected to be an object with three fields
            let obj = msg
                .arguments
                .get(0)
                .and_then(|v| v.as_object())
                .ok_or("shuffleDone expects an object payload")?;

            // 1) encrypted_deck: array of [flag, role] pairs
            let enc = obj
                .get("encrypted_deck")
                .and_then(|v| v.as_array())
                .ok_or("shuffleDone.encrypted_deck missing or not an array")?;
            let encrypted_deck: Vec<[String; 2]> = enc
                .iter()
                .map(|entry| {
                    let arr = entry
                        .as_array()
                        .expect("each deck entry must be a 2-element array");
                    let a = arr[0].as_str().unwrap().to_string();
                    let b = arr[1].as_str().unwrap().to_string();
                    [a, b]
                })
                .collect();

            // 2) public_inputs: array of hex strings
            let pi = obj
                .get("public_inputs")
                .and_then(|v| v.as_array())
                .ok_or("shuffleDone.public_inputs missing or not an array")?;
            let public_inputs: Vec<String> =
                pi.iter().map(|v| v.as_str().unwrap().to_string()).collect();

            // 3) proof: single string
            let proof = obj
                .get("proof")
                .and_then(|v| v.as_str())
                .ok_or("shuffleDone.proof missing or not a string")?
                .to_string();

            Ok(ClientEvent::ShuffleDone {
                encrypted_deck,
                public_inputs,
                proof,
            })
        }
        _ => Ok(ClientEvent::RawUnknown),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn join_event_parses() {
        let frame = json!({
            "type": 1,
            "target": "join",
            "arguments": [ { "name": "Effy" } ]
        });

        let inc: Incoming = serde_json::from_value(frame).unwrap();
        let evt = crate::message::to_client_event(inc).unwrap();

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
