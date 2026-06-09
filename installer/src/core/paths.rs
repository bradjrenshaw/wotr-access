use std::path::PathBuf;

pub const GITHUB_RELEASES_URL: &str =
    "https://api.github.com/repos/bradjrenshaw/wotr-access/releases";
pub const GAME_DIR_NAME: &str = "Pathfinder Second Adventure";
pub const GAME_EXE: &str = "Wrath.exe";
pub const MOD_NAME: &str = "WrathAccess";

/// Native screen-reader DLLs the mod P/Invokes — they must sit next to Wrath.exe.
pub const GAME_ROOT_DLLS: &[&str] = &["Tolk.dll", "nvdaControllerClient64.dll", "SAAPI64.dll"];

/// The game's LocalLow data root, where the native mod system lives.
pub fn locallow_game_dir() -> PathBuf {
    dirs::home_dir()
        .unwrap_or_else(|| PathBuf::from("C:\\Users\\Default"))
        .join("AppData")
        .join("LocalLow")
        .join("Owlcat Games")
        .join("Pathfinder Wrath Of The Righteous")
}

pub fn modifications_dir() -> PathBuf {
    locallow_game_dir().join("Modifications")
}

pub fn installed_mod_dir() -> PathBuf {
    modifications_dir().join(MOD_NAME)
}

/// The mod-manager settings file holding EnabledModifications.
pub fn manager_settings_path() -> PathBuf {
    locallow_game_dir().join("OwlcatModificationManagerSettings.json")
}

pub fn steam_defaults() -> Vec<PathBuf> {
    vec![
        PathBuf::from("C:\\Program Files (x86)\\Steam"),
        PathBuf::from("C:\\Program Files\\Steam"),
    ]
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn locallow_paths_nest() {
        assert!(installed_mod_dir().starts_with(modifications_dir()));
        assert!(modifications_dir().starts_with(locallow_game_dir()));
        assert!(manager_settings_path()
            .to_string_lossy()
            .ends_with("OwlcatModificationManagerSettings.json"));
    }
}
