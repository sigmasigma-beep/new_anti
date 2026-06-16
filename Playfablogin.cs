using System;
using System.Collections;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using PlayFab;
using PlayFab.ClientModels;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class Playfablogin : MonoBehaviourPunCallbacks
{
    public static Playfablogin instance;

    [Header("COSMETICS")]
    public string MyPlayFabID;
    public string CatalogName;
    public List<GameObject> specialitems = new List<GameObject>();
    public List<GameObject> disableitems = new List<GameObject>();

    [Header("SPECIAL ITEM TAGS")]
    [Tooltip("Configure optional leaderboard tags for inventory-backed special items.")]
    public List<SpecialItemRule> specialItemRules = new List<SpecialItemRule>();

    [Header("CURRENCY")]
    public string CurrencyName;
    public TextMeshPro currencyText;
    public string currencyCode = "HS";
    [SerializeField] public int coins;

    [Header("BANNED")]
    public string bannedscenename;

    [Header("TITLE DATA")]
    public TextMeshPro MOTDText;

    [Header("VERSION")]
    [SerializeField] private bool blockOldQuestVersions = true;

    [Header("WHITELIST RULES")]
    [Tooltip("OFF = unknown cosmetics allowed. ON = block everything not in this list.")]
    public bool strictWhitelist;
    [Tooltip("Cosmetic name -> required PlayFab item id rules checked when cosmetics change and every 5 minutes.")]
    public List<CosmeticRule> cosmeticRules = new List<CosmeticRule>();

    [Header("COSMETIC PARENTS")]
    [Tooltip("Cosmetic parent/category names, for example Head, Face, Body, LeftHand, or RightHand. These are strings to avoid scene hierarchy searches.")]
    public List<string> cosmeticParents = new List<string>();

    [Header("SCAN PERFORMANCE")]
    [Tooltip("How often the whitelist is rechecked after Photon connects, in seconds.")]
    [SerializeField] private float whitelistCheckInterval = 300f;

    public static HashSet<string> GrantedItems { get; private set; } = new HashSet<string>();
    public static Dictionary<string, string> EquippedCosmetics { get; private set; } = new Dictionary<string, string>();
    public static bool AntiCheatReady { get; private set; }
    public static string LocalRole { get; private set; } = "Player";
    public static string LocalLeaderboardTag { get; private set; } = string.Empty;
    public static string LocalLeaderboardTagColorHtml { get; private set; } = "#FFFFFF";

    [Serializable]
    public class SpecialItemRule
    {
        [Tooltip("PlayFab Catalog ItemId. If empty, the assigned GameObject name is used.")]
        public string itemId;
        public GameObject itemObject;
        public bool hasTag;
        public string tagText;
        public Color tagColor = Color.white;
    }

    [Serializable]
    public class CosmeticRule
    {
        public string cosmeticName;
        public string requiredInventoryItem;
    }

    [Serializable]
    private class ValidateInventoryFunctionRequest
    {
        public List<string> activeSpecialItems = new List<string>();
        public List<string> equippedCosmetics = new List<string>();
        public Dictionary<string, string> specialItemRequirements = new Dictionary<string, string>();
        public Dictionary<string, string> cosmeticRequirements = new Dictionary<string, string>();
        public string questVersion;
    }

    private readonly Dictionary<string, CosmeticRule> _cosmeticLookup = new Dictionary<string, CosmeticRule>();
    private readonly Dictionary<string, SpecialItemRule> _specialRuleLookup = new Dictionary<string, SpecialItemRule>();
    private bool _inventoryRequestInProgress;
    private bool _hasInventorySnapshot;
    private bool _photonConnectionCheckInProgress;
    private bool _photonConnectionCheckComplete;
    private Coroutine _whitelistLoop;

    public void Awake()
    {
        instance = this;
        BuildLookups();
    }

    private void Start()
    {
        CheckQuestVersion();
        login();
    }

    private void OnDisable()
    {
        StopWhitelistLoop();
    }

    private void OnDestroy()
    {
        StopWhitelistLoop();
    }

    public override void OnConnectedToMaster()
    {
        RequestPhotonConnectionCheck();
    }

    public override void OnJoinedRoom()
    {
        RequestPhotonConnectionCheck();
    }

    public void login()
    {
        var request = new LoginWithCustomIDRequest
        {
            CustomId = SystemInfo.deviceUniqueIdentifier,
            CreateAccount = true,
            InfoRequestParameters = new GetPlayerCombinedInfoRequestParams { GetPlayerProfile = true }
        };
        PlayFabClientAPI.LoginWithCustomID(request, OnLoginSuccess, OnError);
    }

    public void OnLoginSuccess(LoginResult result)
    {
        Debug.Log("logging in");
        PlayFabClientAPI.GetAccountInfo(new GetAccountInfoRequest(), AccountInfoSuccess, OnError);
        GetVirtualCurrencies();
        GetMOTD();
    }

    public void AccountInfoSuccess(GetAccountInfoResult result)
    {
        MyPlayFabID = result.AccountInfo.PlayFabId;
        RefreshSnapshot();
    }

    public void RefreshSnapshot()
    {
        if (_inventoryRequestInProgress) return;
        _inventoryRequestInProgress = true;
        PlayFabClientAPI.GetUserInventory(new GetUserInventoryRequest(), OnInventorySnapshot, error =>
        {
            _inventoryRequestInProgress = false;
            OnError(error);
        });
    }

    private void OnInventorySnapshot(GetUserInventoryResult result)
    {
        _inventoryRequestInProgress = false;
        _hasInventorySnapshot = true;
        AntiCheatReady = true;
        GrantedItems.Clear();

        if (result.Inventory != null)
        {
            foreach (var item in result.Inventory)
            {
                if (string.IsNullOrEmpty(CatalogName) || item.CatalogVersion == CatalogName)
                    GrantedItems.Add(item.ItemId);
            }
        }

        coins = result.VirtualCurrency != null && result.VirtualCurrency.ContainsKey(currencyCode) ? result.VirtualCurrency[currencyCode] : coins;
        if (currencyText != null) currencyText.text = "You have " + coins + " " + CurrencyName;

        BuildLookups();
        ApplySpecialItems();
        UpdateRoleAndLeaderboardTag();
        BroadcastPlayFabProperties();
        RequestPhotonConnectionCheck();
    }

    private void ApplySpecialItems()
    {
        foreach (GameObject item in specialitems)
        {
            if (item == null) continue;
            item.SetActive(GrantedItems.Contains(item.name));
        }

        foreach (GameObject item in disableitems)
        {
            if (item == null) continue;
            if (GrantedItems.Contains(item.name)) item.SetActive(false);
        }

        foreach (SpecialItemRule rule in specialItemRules)
        {
            if (rule == null || rule.itemObject == null) continue;
            rule.itemObject.SetActive(GrantedItems.Contains(GetSpecialItemId(rule)));
        }
    }

    private void UpdateRoleAndLeaderboardTag()
    {
        LocalRole = GrantedItems.Contains("Owner") ? "Owner" : GrantedItems.Contains("Admin") ? "Admin" : GrantedItems.Contains("Manager") ? "Manager" : GrantedItems.Contains("Mod") ? "Mod" : GrantedItems.Contains("YT") ? "YT" : "Player";
        LocalLeaderboardTag = string.Empty;
        LocalLeaderboardTagColorHtml = "#FFFFFF";

        foreach (SpecialItemRule rule in specialItemRules)
        {
            if (rule == null || !rule.hasTag || string.IsNullOrEmpty(rule.tagText)) continue;
            if (!GrantedItems.Contains(GetSpecialItemId(rule))) continue;
            LocalLeaderboardTag = rule.tagText;
            LocalLeaderboardTagColorHtml = "#" + ColorUtility.ToHtmlStringRGB(rule.tagColor);
            break;
        }
    }

    private void BroadcastPlayFabProperties()
    {
        if (!PhotonNetwork.IsConnected || PhotonNetwork.LocalPlayer == null) return;
        Hashtable hash = PhotonNetwork.LocalPlayer.CustomProperties;
        hash["PlayfabID"] = MyPlayFabID;
        hash["Role"] = LocalRole;
        hash["LeaderboardTag"] = LocalLeaderboardTag;
        hash["LeaderboardTagColor"] = LocalLeaderboardTagColorHtml;
        PhotonNetwork.LocalPlayer.SetCustomProperties(hash);
    }

    private void BuildLookups()
    {
        _cosmeticLookup.Clear();
        foreach (CosmeticRule rule in cosmeticRules)
        {
            if (rule != null && !string.IsNullOrEmpty(rule.cosmeticName)) _cosmeticLookup[rule.cosmeticName] = rule;
        }

        _specialRuleLookup.Clear();
        foreach (SpecialItemRule rule in specialItemRules)
        {
            if (rule == null) continue;
            string itemId = GetSpecialItemId(rule);
            if (!string.IsNullOrEmpty(itemId)) _specialRuleLookup[itemId] = rule;
        }
    }

    private string GetSpecialItemId(SpecialItemRule rule)
    {
        if (!string.IsNullOrEmpty(rule.itemId)) return rule.itemId;
        return rule.itemObject != null ? rule.itemObject.name : string.Empty;
    }

    private void RequestPhotonConnectionCheck()
    {
        if (!_hasInventorySnapshot || !PhotonNetwork.IsConnected || _photonConnectionCheckInProgress) return;
        if (_photonConnectionCheckComplete)
        {
            BroadcastPlayFabProperties();
            return;
        }

        _photonConnectionCheckInProgress = true;
        StartCoroutine(PhotonConnectionCheck());
    }

    private IEnumerator PhotonConnectionCheck()
    {
        CheckQuestVersion();
        BroadcastPlayFabProperties();
        yield return ExecuteServerValidation();
        ValidateConfiguredObjects();
        ValidateEquippedCosmetics();
        _photonConnectionCheckComplete = true;
        _photonConnectionCheckInProgress = false;
        StartWhitelistLoop();
    }

    private void ValidateConfiguredObjects()
    {
        foreach (GameObject item in specialitems)
            ValidateActiveInventoryObject(item, item != null ? item.name : string.Empty);

        foreach (SpecialItemRule rule in specialItemRules)
            if (rule != null) ValidateActiveInventoryObject(rule.itemObject, GetSpecialItemId(rule));
    }

    private void ValidateActiveInventoryObject(GameObject item, string requiredItemId)
    {
        if (item == null || !item.activeSelf || string.IsNullOrEmpty(requiredItemId)) return;
        if (!GrantedItems.Contains(requiredItemId)) AntiCheatFail("Active locked item without inventory grant: " + requiredItemId);
    }

    private void StartWhitelistLoop()
    {
        if (_whitelistLoop == null) _whitelistLoop = StartCoroutine(WhitelistLoop());
    }

    private void StopWhitelistLoop()
    {
        if (_whitelistLoop == null) return;
        StopCoroutine(_whitelistLoop);
        _whitelistLoop = null;
    }

    private IEnumerator WhitelistLoop()
    {
        while (_hasInventorySnapshot)
        {
            yield return new WaitForSecondsRealtime(whitelistCheckInterval);
            CheckQuestVersion();
            ValidateEquippedCosmetics();
            ValidateCosmeticParents();
            yield return ExecuteServerValidation();
        }
        _whitelistLoop = null;
    }

    private void ValidateCosmeticParents()
    {
        foreach (string parentName in cosmeticParents)
        {
            if (string.IsNullOrEmpty(parentName)) continue;
            if (EquippedCosmetics.TryGetValue(parentName, out string cosmeticName)) ValidateCosmeticName(cosmeticName);
        }
    }

    private void ValidateEquippedCosmetics()
    {
        foreach (string cosmeticName in EquippedCosmetics.Values) ValidateCosmeticName(cosmeticName);
    }

    private void ValidateCosmeticName(string cosmeticName)
    {
        if (string.IsNullOrEmpty(cosmeticName)) return;
        if (!_cosmeticLookup.TryGetValue(cosmeticName, out CosmeticRule rule))
        {
            if (strictWhitelist) AntiCheatFail("Unknown cosmetic: " + cosmeticName);
            return;
        }
        if (!string.IsNullOrEmpty(rule.requiredInventoryItem) && !GrantedItems.Contains(rule.requiredInventoryItem)) AntiCheatFail("Blocked cosmetic: " + cosmeticName);
    }

    public bool TryChangeCosmetic(string category, string cosmeticName)
    {
        SetEquippedCosmetic(category, cosmeticName);
        ValidateCosmeticName(cosmeticName);
        return IsCosmeticAllowed(cosmeticName);
    }

    public void SetEquippedCosmetic(string category, string cosmeticName)
    {
        EquippedCosmetics[category] = cosmeticName;
    }

    public bool IsCosmeticAllowed(string cosmeticName)
    {
        if (!_hasInventorySnapshot) return false;
        if (!_cosmeticLookup.TryGetValue(cosmeticName, out CosmeticRule rule)) return !strictWhitelist;
        return string.IsNullOrEmpty(rule.requiredInventoryItem) || GrantedItems.Contains(rule.requiredInventoryItem);
    }

    private IEnumerator ExecuteServerValidation()
    {
        bool complete = false;
        bool allowed = true;
        string reason = string.Empty;
        ValidateInventoryFunctionRequest functionRequest = new ValidateInventoryFunctionRequest { questVersion = SystemInfo.operatingSystem };
        CollectActiveSpecialItems(functionRequest.activeSpecialItems);
        CollectActiveCosmetics(functionRequest.equippedCosmetics);
        CollectServerRequirementMaps(functionRequest.specialItemRequirements, functionRequest.cosmeticRequirements);

        PlayFabClientAPI.ExecuteCloudScript(new ExecuteCloudScriptRequest
        {
            FunctionName = "ValidateInventoryWhitelist",
            FunctionParameter = functionRequest,
            GeneratePlayStreamEvent = true
        }, result =>
        {
            complete = true;
            if (result.Error != null)
            {
                allowed = false;
                reason = result.Error.Message;
            }
        }, error =>
        {
            complete = true;
            allowed = false;
            reason = error.GenerateErrorReport();
        });

        while (!complete) yield return null;
        if (!allowed) AntiCheatFail("Server validation failed: " + reason);
    }

    private void CollectServerRequirementMaps(Dictionary<string, string> specialRequirements, Dictionary<string, string> cosmeticRequirements)
    {
        foreach (SpecialItemRule rule in specialItemRules)
        {
            if (rule == null) continue;
            string itemId = GetSpecialItemId(rule);
            if (!string.IsNullOrEmpty(itemId)) specialRequirements[itemId] = itemId;
        }

        foreach (CosmeticRule rule in cosmeticRules)
        {
            if (rule == null || string.IsNullOrEmpty(rule.cosmeticName) || string.IsNullOrEmpty(rule.requiredInventoryItem)) continue;
            cosmeticRequirements[rule.cosmeticName] = rule.requiredInventoryItem;
        }
    }

    private void CollectActiveSpecialItems(List<string> output)
    {
        foreach (GameObject item in specialitems) if (item != null && item.activeSelf) output.Add(item.name);
        foreach (SpecialItemRule rule in specialItemRules) if (rule != null && rule.itemObject != null && rule.itemObject.activeSelf) output.Add(GetSpecialItemId(rule));
    }

    private void CollectActiveCosmetics(List<string> output)
    {
        foreach (string cosmetic in EquippedCosmetics.Values) if (!string.IsNullOrEmpty(cosmetic)) output.Add(cosmetic);
        foreach (string parentName in cosmeticParents)
        {
            if (string.IsNullOrEmpty(parentName)) continue;
            if (EquippedCosmetics.TryGetValue(parentName, out string cosmeticName) && !string.IsNullOrEmpty(cosmeticName)) output.Add(cosmeticName);
        }
    }

    public void GetVirtualCurrencies()
    {
        PlayFabClientAPI.GetUserInventory(new GetUserInventoryRequest(), OnGetUserInventorySuccess, OnError);
    }

    private void OnGetUserInventorySuccess(GetUserInventoryResult result)
    {
        if (result.VirtualCurrency != null && result.VirtualCurrency.ContainsKey(currencyCode)) coins = result.VirtualCurrency[currencyCode];
        if (currencyText != null) currencyText.text = "You have " + coins + " " + CurrencyName;
    }

    private void OnError(PlayFabError error)
    {
        Debug.LogError(error.GenerateErrorReport());
        if (error.Error == PlayFabErrorCode.AccountBanned) SceneManager.LoadScene(bannedscenename);
    }

    public void GetMOTD()
    {
        PlayFabClientAPI.GetTitleData(new GetTitleDataRequest(), MOTDGot, OnError);
    }

    public void MOTDGot(GetTitleDataResult result)
    {
        if (result.Data == null || !result.Data.ContainsKey("MOTD"))
        {
            Debug.Log("No MOTD");
            return;
        }
        if (MOTDText != null) MOTDText.text = result.Data["MOTD"];
    }

    private void CheckQuestVersion()
    {
        if (!blockOldQuestVersions) return;
        string osVersion = SystemInfo.operatingSystem;
        if (osVersion.Contains("v78") || osVersion.Contains("v79") || osVersion.Contains("/78.") || osVersion.Contains("/79.")) AntiCheatFail("Blocked Quest version: " + osVersion);
    }

    public void AntiCheatFail(string reason)
    {
        Debug.LogError("[ANTICHEAT FAIL] " + reason);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.ExitPlaymode();
#else
        UnityEngine.Diagnostics.Utils.ForceCrash(UnityEngine.Diagnostics.ForcedCrashCategory.FatalError);
#endif
    }
}
