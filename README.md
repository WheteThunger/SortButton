## Features

- Adds a sort button to storage boxes
- Allows sorting by item name or category
- Automatically consolidates separate stacks of the same item when sorting
- Allows restricting sorting ability to container owner and teammates/friends/clanmates

![](https://i.imgur.com/dPeNv8G.png)

## Permissions

* `sortbutton.use` - Allows players to see the sort button and use all features of the plugin

## Commands

* `sortbutton` - Enable/Disable Sort Button.
* `sortbutton <sort | type>` - change sort type.

## Configuration

Default configuration:

```json
{
  "Default enabled": true,
  "Default sort by category": true,
  "Check ownership": true,
  "Use Clans": true,
  "Use Friends": true,
  "Use Teams": true,
  "Chat steamID icon": 0,
  "Chat command": [
    "sortbutton"
  ],
  "Containers by short prefab name": {
    "assets/content/vehicles/boats/rhib/subents/rhib_storage.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/content/vehicles/boats/rowboat/subents/rowboat_storage.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/content/vehicles/horse/ridablehorse.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/content/vehicles/modularcar/subents/modular_car_1mod_storage.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/content/vehicles/modularcar/subents/modular_car_camper_storage.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/content/vehicles/snowmobiles/subents/snowmobileitemstorage.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/content/vehicles/submarine/subents/submarineitemstorage.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/composter/composter.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/dropbox/dropbox.deployed.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/fridge/fridge.deployed.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/hitch & trough/hitchtrough.deployed.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/hot air balloon/subents/hab_storage.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/large wood storage/skins/abyss_dlc_large_wood_box/abyss_dlc_storage_horizontal/abyss_barrel_horizontal.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/large wood storage/skins/abyss_dlc_large_wood_box/abyss_dlc_storage_vertical/abyss_barrel_vertical.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/large wood storage/skins/jungle_dlc_large_wood_box/jungle_dlc_storage_horizontal/wicker_barrel.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/large wood storage/skins/jungle_dlc_large_wood_box/jungle_dlc_storage_vertical/bamboo_barrel.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/large wood storage/skins/medieval_large_wood_box/medieval.box.wooden.large.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/large wood storage/skins/warhammer_dlc_large_wood_box/krieg_storage_horizontal/krieg_storage_horizontal.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/large wood storage/skins/warhammer_dlc_large_wood_box/krieg_storage_vertical/krieg_storage_vertical.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/minifridge/minifridge.deployed.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/small stash/small_stash_deployed.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/tool cupboard/cupboard.tool.deployed.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/tool cupboard/retro/cupboard.tool.retro.deployed.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/tool cupboard/shockbyte/cupboard.tool.shockbyte.deployed.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/vendingmachine/vendingmachine.deployed.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/wall cabinet/electric.wallcabinet.deployed.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/woodenbox/skins/pilot_hazmat_wooden_box/pilot_hazmat_woodbox_deployed.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/misc/decor_dlc/storagebarrel/storage_barrel_b.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/misc/decor_dlc/storagebarrel/storage_barrel_c.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    },
    "assets/prefabs/misc/halloween/coffin/coffinstorage.prefab": {
      "Enabled": true,
      "OffsetX": 476.5
    }
  },
  "Containers by skin ID": {}
}
```

- `Default enabled` (`true` or `false`) -- While `true`, the sort button will be enabled for players by default. Players can toggle the sort button with the `sortbutton` command.
- `Default sort by category` (`true` or `false`) -- While `true`, the sort button will use category mode by default. Players can toggle the mode with the `sortbutton <sort | type>` command.
- `Check ownership` (`true` or `false`) -- While `true`, players can only sort containers owned by them or teammates/friends/clanmates (if those settings are enabled). Regardless of this setting, all players can sort unowned containers such as vehicle containers.
- `Use Clans` (`true` or `false`) -- While `true`, players can sort containers owned by their clanmates while `Check ownership` is enabled.
- `Use Friends` (`true` or `false`) -- While `true`, players can sort containers owned by their friends while `Check ownership` is enabled.
- `Use Teams` (`true` or `false`) -- While `true`, players can sort containers owned by their teammates while `Check ownership` is enabled.
- `Chat steamID icon` -- Determines the icon that will be printed in chat when the player uses the `sortbutton` command.
- `Chat command` -- Determines which commands players can use to change sort button functionality. By default, only the `"sortbutton"` command is available, but you can add more such as `"sb"` if you want.
- `Containers by short prefab name` -- This section allows you to configure whether the sort button will be enabled for each type of container, as well as where the button will be positioned.
  - `Enable` (`true` or `false`) -- While `true`, the sort button will appear for this type of container, as long as the player has the sort button enabled.
  - `OffsetX` -- Determines where the sort button will be horizontally positioned (relative to the center of the screen). The vertical position is determined automatically based on the type of container and its capacity.
- `Containers by skin ID` -- This section works like `Containers by short prefab name`, but allows you to override the behavior for containers with specific skin IDs. This is useful for special containers managed by other plugins, as long as they have a consistent skin or range of skins.

## Localization

```json
{
  "Error.NoPermission": "You do not have permission to use this command",
  "Format.ButtonText": "Sort",
  "Format.Category": "<color=#D2691E>Category</color>",
  "Format.Disabled": "<color=#B22222>Disabled</color>",
  "Format.Enabled": "<color=#228B22>Enabled</color>",
  "Format.Name": "<color=#00BFFF>Name</color>",
  "Format.Prefix": "<color=#00FF00>[Sort Button]</color>: ",
  "Info.ButtonStatus": "Sort Button is now {0}",
  "Info.SortType": "Sort Type is now {0}",
  "Info.Help": "List Commands:\n<color=#FFFF00>/{0}</color> - Enable/Disable Sort Button.\n<color=#FFFF00>/{0} <sort | type></color> - change sort type."
}
```

## Credits

* **fullbanner**, the original author of this plugin
* [**MJSU**](https://umod.org/user/MJSU) many thanks for all help
* [**MONaH**](https://umod.org/user/MONaH) for maintaining this plugin
