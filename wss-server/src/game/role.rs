use crate::types::{PlayerId, Role};
use rand::seq::SliceRandom;
use std::collections::HashMap;

/// Assign roles to players randomly
pub fn assign_roles(player_ids: &[PlayerId]) -> HashMap<PlayerId, Role> {
    let mut roles = generate_roles(player_ids.len());
    let mut rng = rand::thread_rng();

    // Shuffle roles
    roles.shuffle(&mut rng);

    // Assign to player IDs
    player_ids.iter().cloned().zip(roles.into_iter()).collect()
}

/// Generate role list based on player count
/// Currently supports 4 players only
fn generate_roles(player_count: usize) -> Vec<Role> {
    match player_count {
        4 => vec![Role::Werewolf, Role::Seer, Role::Villager, Role::Villager],
        _ => panic!("Unsupported player count: {}", player_count),
    }
}
