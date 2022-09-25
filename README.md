**Quick Sort** adds a new GUI menu when looting containers and players. Players can choose what type of items they want to put in the container, or even loot all items from the container. Also supports automated looting containers that isn't accepting players items.
![](https://i.imgur.com/9XtWReC.jpg)

## Permissions

- `quicksort.use` -- Allows player to use Quick Sort UI and features
- `quicksort.lootall` -- Allows player use Loot All feature
- `quicksort.autolootall` -- Allows player use Automated Loot All feature

## Commands

* `/qs on` - Enable GUI
* `/qs off` - Disable GUI
* `/qs auto` - toggle automated looting
* `/qs <s | style> <center | lite | right | custom>` - change GUI style
* `/qs <c | conatiner> <main | wear | belt>` - add/remove container type from the sort (if they are enabled in the configuration).

## Configuration
```json
{
{
  "Global settings": {
    "Default enabled": true,
    "Default UI style (center, lite, right, custom)": "right",
    "Loot all delay in seconds (0 to disable)": 0,
    "Enable loot all on the sleepers": false,
    "Auto loot all enabled by default": false,
    "Default enabled container types": {
      "Belt": false,
      "Main": true,
      "Wear": false
    },
    "Chat steamID icon": 0,
    "Chat command": [
      "qs",
      "quicksort"
    ],
    "Excluded containers": [
      "assets/prefabs/deployable/single shot trap/guntrap.deployed.prefab",
      "assets/prefabs/npc/autoturret/autoturret_deployed.prefab",
      "assets/prefabs/npc/flame turret/flameturret.deployed.prefab",
      "assets/prefabs/npc/sam_site_turret/sam_site_turret_deployed.prefab",
      "assets/prefabs/npc/sam_site_turret/sam_static.prefab"
    ]
  },
  "Custom UI Settings": {
    "AnchorsMin": "0.5 1.0",
    "AnchorsMax": "0.5 1.0",
    "OffsetsMin": "192 -137",
    "OffsetsMax": "573 0",
    "Color": "0.5 0.5 0.5 0.33",
    "ButtonsColor": "0.75 0.43 0.18 0.8",
    "LootAllColor": "0.41 0.50 0.25 0.8",
    "TextColor": "0.77 0.92 0.67 0.8",
    "TextSize": 16,
    "CategoriesTextSize": 14
  }
}
```

## Localization
```json
{
  "Error.NoPermission": "You do not have permission to use this command!",
  "Format.All": "All",
  "Format.Ammo": "Ammo",
  "Format.Attire": "Attire",
  "Format.Components": "Components",
  "Format.Construction": "Construction",
  "Format.Deployables": "Deployables",
  "Format.Deposit": "Deposit",
  "Format.Disabled": "<color=#B22222>Disabled</color>",
  "Format.Electrical": "Electrical",
  "Format.Enabled": "<color=#228B22>Enabled</color>",
  "Format.Existing": "Existing",
  "Format.Food": "Food",
  "Format.LootAll": "Loot All",
  "Format.Medical": "Medical",
  "Format.Misc": "Misc",
  "Format.Prefix": "<color=#00FF00>[Quick Sort]</color>: ",
  "Format.Resources": "Resources",
  "Format.Tools": "Tools",
  "Format.Traps": "Traps",
  "Format.Weapons": "Weapons",
  "Info.AutoLootAll": "Automated looting is now {0}",
  "Info.ContainerType": "Quick Sort for container type {0} is now {1}",
  "Info.QuickSort": "Quick Sort GUI is now {0}",
  "Info.Style": "Quick Sort GUI style is now {0}",
  "Error.Syntax": "List Commands:\n<color=#FFFF00>/{0} on</color> - Enable GUI\n<color=#FFFF00>/{0} off</color> - Disable GUI\n<color=#FFFF00>/{0} auto</color> - Enable/Disable automated looting\n<color=#FFFF00>/{0} <s | style> <center | lite | right | custom></color> - change GUI style\n<color=#FFFF00>/{0} <c | conatiner> <main | wear | belt></color> - add/remove container type from the sort"
}
```

## Credits

- **emu**, for the original version of this plugin
- **Wulf**, the original author of the re-written version of this plugin