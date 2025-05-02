use crate::types::{PlayerId, Role};
use rand::seq::SliceRandom;
use std::collections::HashMap;

pub fn assign_roles(player_ids: &[PlayerId]) -> HashMap<PlayerId, Role> {
    let mut roles = generate_roles(player_ids.len());
    let mut rng = rand::rng();

    roles.shuffle(&mut rng);

    player_ids.iter().cloned().zip(roles.into_iter()).collect()
}

fn generate_roles(player_count: usize) -> Vec<Role> {
    match player_count {
        4 => vec![Role::Werewolf, Role::Seer, Role::Villager, Role::Villager],
        _ => panic!("Unsupported player count: {}", player_count),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::types::Role;
    use std::collections::HashMap;

    #[test]
    fn test_assign_roles_for_4_players() {
        let player_ids = vec![
            "p1".to_string(),
            "p2".to_string(),
            "p3".to_string(),
            "p4".to_string(),
        ];

        let role_map = assign_roles(&player_ids);

        assert_eq!(role_map.len(), 4);

        let mut counts = HashMap::new();
        for role in role_map.values() {
            *counts.entry(role).or_insert(0) += 1;
        }

        assert_eq!(counts.get(&Role::Werewolf), Some(&1));
        assert_eq!(counts.get(&Role::Seer), Some(&1));
        assert_eq!(counts.get(&Role::Villager), Some(&2));
    }

    #[test]
    fn test_role_assignment_randomness() {
        let ids = vec!["a".into(), "b".into(), "c".into(), "d".into()];
        let r1 = assign_roles(&ids);
        let r2 = assign_roles(&ids);

        assert_ne!(
            r1, r2,
            "Role assignment should usually be different between runs"
        );
    }
}
