// Upload this function to PlayFab CloudScript as ValidateInventoryWhitelist.
// Keep the required item ids here in sync with the Unity inspector rules so the
// final allow/deny decision is made with the player's server-side inventory.
handlers.ValidateInventoryWhitelist = function (args, context) {
    var inventory = server.GetUserInventory({ PlayFabId: currentPlayerId });
    var ownedItems = {};

    for (var i = 0; i < inventory.Inventory.length; i++) {
        ownedItems[inventory.Inventory[i].ItemId] = true;
    }

    var specialItemRequirements = args.specialItemRequirements || {};
    var cosmeticRequirements = args.cosmeticRequirements || {};
    var activeSpecialItems = args.activeSpecialItems || [];
    var equippedCosmetics = args.equippedCosmetics || [];
    var questVersion = args.questVersion || "";

    if (questVersion.indexOf("v78") !== -1 || questVersion.indexOf("v79") !== -1 || questVersion.indexOf("/78.") !== -1 || questVersion.indexOf("/79.") !== -1) {
        throw "Blocked Quest version: " + questVersion;
    }

    for (var s = 0; s < activeSpecialItems.length; s++) {
        var specialItem = activeSpecialItems[s];
        var requiredSpecialItem = specialItemRequirements[specialItem] || specialItem;
        if (requiredSpecialItem && !ownedItems[requiredSpecialItem]) {
            throw "Active locked item without inventory grant: " + requiredSpecialItem;
        }
    }

    for (var c = 0; c < equippedCosmetics.length; c++) {
        var cosmetic = equippedCosmetics[c];
        var requiredCosmeticItem = cosmeticRequirements[cosmetic];
        if (requiredCosmeticItem && !ownedItems[requiredCosmeticItem]) {
            throw "Blocked cosmetic: " + cosmetic;
        }
    }

    return { allowed: true };
};
