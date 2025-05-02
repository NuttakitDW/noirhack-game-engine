use std::sync::{Arc, Mutex};

/// The single game room state
#[derive(Default)]
pub struct Room {
    // for now, still empty—fields will come later
}

/// A clonable, thread-safe handle to the one shared Room
pub type SharedRoom = Arc<Mutex<Room>>;

impl Room {
    /// Constructor for a fresh Room
    pub fn new() -> Self {
        Room::default()
    }
}
