using MelonLoader;
using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.UI.Handover;
using Il2CppScheduleOne.Properties;

using static TopShelfBonus_IL2CPP.TopShelfExtensions;
using static TopShelfBonus_IL2CPP.TopShelfUtilities;
using UnityEngine;
using Il2CppScheduleOne.UI.Phone.ContactsApp;
using UnityEngine.UI;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.DevUtilities;
using UnityEngine.EventSystems;
using Il2CppScheduleOne.UI.Phone;
using Il2CppScheduleOne.UI.Phone.Messages;
using Il2CppScheduleOne.UI;
using CanvasScaler = UnityEngine.UI.CanvasScaler;
using Il2CppScheduleOne;
using Il2CppNewtonsoft.Json.Linq;
using Il2CppScheduleOne.Persistence.Loaders;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Law;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.NPCs.Relation;
using Il2CppInterop.Runtime;
using UnityEngine.Events;
using static Il2CppScheduleOne.Quests.Contract;

[assembly: MelonInfo(typeof(TopShelfBonus_IL2CPP.TopShelfBonus), "TopShelfBonus_Standard", "1.1.3", "Archie", "Adds 5% bonus for delivering items with quality above the customer's required standards.")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: HarmonyDontPatchAll]
namespace TopShelfBonus_IL2CPP
{
  public static class DebugConfig
  {
    public static bool EnableDebugLogs = false;
  }

  public class TopShelfBonus : MelonMod
  {
    public override void OnInitializeMelon()
    {
      try
      {
        HarmonyInstance.PatchAll();
        MelonLogger.Msg("TopShelfBonus_Standard initialized.");
      }
      catch (Exception e)
      {
        MelonLogger.Error($"Failed to initialize TopShelfBonus_Standard: {e}");
      }
      TopShelfConfig.Initialize();
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
      base.OnSceneWasLoaded(buildIndex, sceneName);
      if (sceneName.ToLower() == "main")
        AffinityPopup.GeneratePrefab();
    }
  }

  public static class TopShelfConfig
  {
    public static MelonPreferences_Category Category;
    public static MelonPreferences_Entry<float> AboveQualityBonus;
    public static MelonPreferences_Entry<float> Matching1Bonus;
    public static MelonPreferences_Entry<float> Matching2Bonus;
    public static MelonPreferences_Entry<float> Matching3Bonus;
    public static MelonPreferences_Entry<float> HatedPenalty;
    public static MelonPreferences_Entry<float> MaxHatedPenalty;
    public static MelonPreferences_Entry<float> NeutralPenalty;
    public static MelonPreferences_Entry<float> MaxNeutralPenalty;
    public static MelonPreferences_Entry<int> IgnoreNeutral;
    public static MelonPreferences_Entry<int> MinHated;
    public static MelonPreferences_Entry<int> MaxHated;
    public static MelonPreferences_Entry<float>[] QualityBonuses;

    public static void Initialize()
    {
      Category = MelonPreferences.CreateCategory("TopShelfBonus");
      AboveQualityBonus = Category.CreateEntry("AboveQualityBonus", 0.05f, "Bonus per quality level above required (e.g., 0.15 = 15%)");
      Matching1Bonus = Category.CreateEntry("Matching1Bonus", 0.05f, "Bonus for 1 matching affinity");
      Matching2Bonus = Category.CreateEntry("Matching2Bonus", 0.12f, "Bonus for 2 matching affinities");
      Matching3Bonus = Category.CreateEntry("Matching3Bonus", 0.25f, "Bonus for 3 matching affinities");
      MinHated = Category.CreateEntry("MinHated", 0, "Minimum number of negatice affinities per customer");
      MaxHated = Category.CreateEntry("MaxHated", 3, "Maximum number of negatice affinities per customer");
      HatedPenalty = Category.CreateEntry("HatedPenalty", -0.15f, "Penalty per hated affinity");
      MaxHatedPenalty = Category.CreateEntry("MaxHatedPenalty", -0.45f, "Max Penalty for hated affinities");
      NeutralPenalty = Category.CreateEntry("NeutralPenalty", -0.03f, "Penalty per neutral affinity");
      MaxNeutralPenalty = Category.CreateEntry("MaxNeutralPenalty", -0.24f, "Max Penalty for neutral affinities");
      IgnoreNeutral = Category.CreateEntry("IgnoreNeutral", 2, "Ignores number of neutral affinity per matching affinity");
      QualityBonuses = new MelonPreferences_Entry<float>[5];
      QualityBonuses[0] = Category.CreateEntry("QualityBonus_Trash", -0.12f, "Bonus for Trash quality");
      QualityBonuses[1] = Category.CreateEntry("QualityBonus_Poor", -0.05f, "Bonus for Poor quality");
      QualityBonuses[2] = Category.CreateEntry("QualityBonus_Standard", 0.00f, "Bonus for Standard quality");
      QualityBonuses[3] = Category.CreateEntry("QualityBonus_Premium", 0.05f, "Bonus for Premium quality");
      QualityBonuses[4] = Category.CreateEntry("QualityBonus_Heavenly", 0.12f, "Bonus for Heavenly quality");
    }
  }

  public static class TopShelfExtensions
  {
    public class PopupHolder : MonoBehaviour
    {
      public static AffinityPopup popup;
    }
    public static GameObject popupPrefab;
    public static Dictionary<Il2CppSystem.Guid, List<Property>> NegativeProperties = new();

    public struct ItemStats
    {
      public int TotalItems;
      public int TotalExcessQuality;
      public int MatchingProperties;
      public int NonMatchingProperties;
      public HashSet<Property> HatedProperties;
      public Il2CppSystem.Collections.Generic.List<ItemInstance> Items;
    }
  }

  public static class TopShelfUtilities
  {
    public static void InitializeNegativeProperties(Customer customer)
    {
      var allProperties = Resources.LoadAll<Property>("Properties").ToList();
      var guid = customer.NPC.GUID;
      NegativeProperties[guid] = new List<Property>();

      // Randomly Assign 0-3 negative properties
      int negativeCount = UnityEngine.Random.Range(TopShelfConfig.MinHated.Value, TopShelfConfig.MaxHated.Value);
      var availableProperties = allProperties.Where(p => !customer.customerData.PreferredProperties.Contains(p)).ToList();
      for (int i = 0; i < negativeCount && availableProperties.Count > 0; i++)
      {
        var prop = availableProperties[UnityEngine.Random.Range(0, availableProperties.Count)];
        NegativeProperties[guid].Add(prop);
        availableProperties.Remove(prop);
      }

      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"Initialized negative properties for customer {customer.NPC.fullName} (GUID: {guid}): {string.Join(", ", NegativeProperties[guid].Select(p => p.Name))}");
    }

    /// <summary>
    /// Converts a System.Collections.Generic.List<T> to an Il2CppSystem.Collections.Generic.List<T>.
    /// </summary>
    /// <typeparam name="T">The type of elements in the list, must inherit from Il2CppSystem.Object.</typeparam>
    /// <param name="systemList">The System list to convert.</param>
    /// <returns>An Il2CppSystem list containing the same elements, or an empty list if the input is null.</returns>
    public static Il2CppSystem.Collections.Generic.List<T> ConvertList<T>(List<T> systemList)
        where T : Il2CppSystem.Object
    {
      if (systemList == null)
        return new Il2CppSystem.Collections.Generic.List<T>();

      Il2CppSystem.Collections.Generic.List<T> il2cppList = new(systemList.Count);
      foreach (var item in systemList)
      {
        if (item != null)
          il2cppList.Add(item);
      }
      return il2cppList;
    }

    /// <summary>
    /// Converts an Il2CppSystem.Collections.Generic.List<T> to a System.Collections.Generic.List<T>.
    /// </summary>
    /// <typeparam name="T">The type of elements in the list, must inherit from Il2CppSystem.Object.</typeparam>
    /// <param name="il2cppList">The Il2CppSystem list to convert.</param>
    /// <returns>A System list containing the same elements, or an empty list if the input is null.</returns>
    public static List<T> ConvertList<T>(Il2CppSystem.Collections.Generic.List<T> il2cppList)
        where T : Il2CppSystem.Object
    {
      if (il2cppList == null)
        return [];

      List<T> systemList = new(il2cppList.Count);
      for (int i = 0; i < il2cppList.Count; i++)
      {
        var item = il2cppList[i];
        if (item != null)
          systemList.Add(item);
      }
      return systemList;
    }
  }

  public static class BonusCalculator
  {
    private const int MaxQualityLevels = 5;

    public static void AddTopShelfBonus(Il2CppSystem.Collections.Generic.List<BonusPayment> bonuses, Customer customer, Contract contract, Il2CppSystem.Collections.Generic.List<ItemInstance> items)
    {
      if (bonuses == null || customer == null || items == null)
      {
        MelonLogger.Error($"BonusCalculator.AddTopShelfBonus: Invalid input: bonuses={bonuses != null}, customer={customer != null}, contract={contract != null}, items={items != null}");
        return;
      }

      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"BonusCalculator.AddTopShelfBonus: Processing for customer={customer.NPC.fullName}, GUID={customer.NPC.GUID}, items={items.Count}, basePayment={contract.Payment:F2}");

      var guid = customer.NPC.GUID;
      if (!NegativeProperties.TryGetValue(guid, out var negativeProperties))
      {
        MelonLogger.Warning($"BonusCalculator.AddTopShelfBonus: No NegativeProperties for customer {customer.NPC.fullName} (GUID: {guid}), initializing empty");
        negativeProperties = new List<Property>();
        NegativeProperties[guid] = negativeProperties;
      }
      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"BonusCalculator.AddTopShelfBonus: NegativeProperties={negativeProperties.Count}, PreferredProperties={customer.customerData.PreferredProperties.Count}, Standards={customer.customerData.Standards}");

      var stats = CalculateItemStats(customer, items, negativeProperties);
      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"BonusCalculator.AddTopShelfBonus: ItemStats - TotalItems={stats.TotalItems}, TotalExcessQuality={stats.TotalExcessQuality}, MatchingProperties={stats.MatchingProperties}, NonMatchingProperties={stats.NonMatchingProperties}, HatedProperties={stats.HatedProperties.Count}");

      AddQualityBonus(bonuses, contract.Payment, stats);
      AddAffinityBonus(bonuses, contract.Payment, stats);
      AddAffinityPenalty(bonuses, contract.Payment, stats);
    }

    private static ItemStats CalculateItemStats(Customer customer, Il2CppSystem.Collections.Generic.List<ItemInstance> items, List<Property> negativeProperties)
    {
      var stats = new ItemStats { HatedProperties = new HashSet<Property>() };
      int requiredQuality = (int)StandardsMethod.GetCorrespondingQuality(customer.customerData.Standards);
      var preferredProperties = new HashSet<Property>(customer.customerData.PreferredProperties._items);
      stats.Items = items;
      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"BonusCalculator.CalculateItemStats: Processing {items.Count} items for customer={customer.NPC.fullName}, RequiredQuality={requiredQuality}, PreferredProperties={preferredProperties.Count}, NegativeProperties={negativeProperties.Count}");

      foreach (var item in items)
      {
        if (item.TryCast<ProductItemInstance>() is ProductItemInstance productItem)
        {
          stats.TotalItems++;
          int itemQuality = (int)productItem.Quality;
          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Msg($"BonusCalculator.CalculateItemStats: Item={productItem.Definition?.Name ?? "null"}, Type={productItem.GetType().FullName}, Quality={itemQuality}, RequiredQuality={requiredQuality}");

          if (itemQuality > requiredQuality)
            stats.TotalExcessQuality += itemQuality - requiredQuality;

          List<Property> properties = productItem.Definition.TryCast<ProductDefinition>()?.Properties._items.ToList() ?? new();
          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Msg($"BonusCalculator.CalculateItemStats: Item={productItem.Definition?.Name ?? "null"}, Properties={properties.Count}");

          foreach (var property in properties)
          {
            if (preferredProperties.Contains(property))
              stats.MatchingProperties++;
            else if (negativeProperties.Contains(property))
              stats.HatedProperties.Add(property);
            else
              stats.NonMatchingProperties++;
          }

          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Msg($"BonusCalculator.CalculateItemStats: Item={productItem.Definition?.Name ?? "null"}, Quality={itemQuality}, TotalExcessQuality={stats.TotalExcessQuality}, Matching={stats.MatchingProperties}, NonMatching={stats.NonMatchingProperties}, Hated={stats.HatedProperties.Count}");
        }
        else
        {
          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Msg($"BonusCalculator.CalculateItemStats: Item failed to cast to ProductItemInstance, Type={item?.GetType().FullName ?? "null"}");
        }
      }

      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"BonusCalculator.CalculateItemStats: Final Stats - TotalItems={stats.TotalItems}, TotalExcessQuality={stats.TotalExcessQuality}, MatchingProperties={stats.MatchingProperties}, NonMatchingProperties={stats.NonMatchingProperties}, HatedProperties={stats.HatedProperties.Count}");
      return stats;
    }

    private static void AddQualityBonus(Il2CppSystem.Collections.Generic.List<BonusPayment> bonuses, float basePayment, ItemStats stats)
    {
      if (stats.TotalItems == 0)
      {
        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg($"BonusCalculator.AddQualityBonus: No items, skipping quality bonus");
        return;
      }

      float qualityBonus = 0;
      float averageQuality = 0;
      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"BonusCalculator.AddQualityBonus: Processing {stats.TotalItems} items, basePayment={basePayment:F2}, QualityBonuses=[{string.Join(", ", TopShelfConfig.QualityBonuses.Select(q => q.Value.ToString("F2")))}], AboveQualityBonus={TopShelfConfig.AboveQualityBonus.Value:F2}");

      foreach (var item in stats.Items)
      {
        var qualityItem = item.TryCast<QualityItemInstance>();
        if (qualityItem == null)
        {
          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Msg($"BonusCalculator.AddQualityBonus: Item={item.Definition?.Name ?? "null"} failed to cast to QualityItemInstance, Type={item?.GetType().FullName ?? "null"}");
          continue;
        }
        int quality = (int)qualityItem.Quality;
        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg($"BonusCalculator.AddQualityBonus: Item={item.Definition?.Name ?? "null"}, Quality={quality}, QualityBonusIndex={quality}, QualityBonusValue={(quality >= 0 && quality < TopShelfConfig.QualityBonuses.Length ? TopShelfConfig.QualityBonuses[quality].Value.ToString("F2") : "invalid")}");

        if (quality >= 0 && quality < TopShelfConfig.QualityBonuses.Length)
          qualityBonus += TopShelfConfig.QualityBonuses[quality].Value;
        else
          MelonLogger.Warning($"BonusCalculator.AddQualityBonus: Invalid quality {quality} for item {item.Definition?.Name ?? "null"}");
        averageQuality += quality;
      }

      if (stats.TotalItems == 0)
      {
        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg($"BonusCalculator.AddQualityBonus: No valid items after processing, skipping");
        return;
      }

      averageQuality /= stats.TotalItems;
      qualityBonus = qualityBonus / stats.TotalItems * basePayment;
      float excessQualityBonus = TopShelfConfig.AboveQualityBonus.Value * averageQuality * basePayment;

      float totalQualityBonus = qualityBonus + excessQualityBonus;
      if (totalQualityBonus != 0)
        bonuses.Add(new BonusPayment("Quality Bonus", totalQualityBonus));
      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"BonusCalculator.AddQualityBonus: Calculated qualityBonus={qualityBonus:F2}, excessQualityBonus={excessQualityBonus:F2}, totalQualityBonus={totalQualityBonus:F2}, averageQuality={averageQuality:F2}, TotalExcessQuality={stats.TotalExcessQuality}");
    }

    private static void AddAffinityBonus(Il2CppSystem.Collections.Generic.List<BonusPayment> bonuses, float basePayment, ItemStats stats)
    {
      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"BonusCalculator.AddAffinityBonus: Processing MatchingProperties={stats.MatchingProperties}, basePayment={basePayment:F2}, Matching1Bonus={TopShelfConfig.Matching1Bonus.Value:F2}, Matching2Bonus={TopShelfConfig.Matching2Bonus.Value:F2}, Matching3Bonus={TopShelfConfig.Matching3Bonus.Value:F2}");

      float affinityBonus = stats.MatchingProperties switch
      {
        >= 3 => TopShelfConfig.Matching3Bonus.Value,
        2 => TopShelfConfig.Matching2Bonus.Value,
        1 => TopShelfConfig.Matching1Bonus.Value,
        _ => 0f
      } * basePayment;

      if (affinityBonus != 0)
      {
        bonuses.Add(new BonusPayment("Affinity Bonus", affinityBonus));
        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg($"BonusCalculator.AddAffinityBonus: Added Affinity Bonus={affinityBonus:F2}, MatchingProperties={stats.MatchingProperties}");
      }
      else if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"BonusCalculator.AddAffinityBonus: No Affinity Bonus applied, MatchingProperties={stats.MatchingProperties}");
    }

    private static void AddAffinityPenalty(Il2CppSystem.Collections.Generic.List<BonusPayment> bonuses, float basePayment, ItemStats stats)
    {
      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"BonusCalculator.AddAffinityPenalty: Processing NonMatchingProperties={stats.NonMatchingProperties}, MatchingProperties={stats.MatchingProperties}, HatedProperties={stats.HatedProperties.Count}, basePayment={basePayment:F2}, NeutralPenalty={TopShelfConfig.NeutralPenalty.Value:F2}, MaxNeutralPenalty={TopShelfConfig.MaxNeutralPenalty.Value:F2}, HatedPenalty={TopShelfConfig.HatedPenalty.Value:F2}, MaxHatedPenalty={TopShelfConfig.MaxHatedPenalty.Value:F2}, IgnoreNeutral={TopShelfConfig.IgnoreNeutral.Value}");

      float neutralPenalty = Math.Max(Math.Max(0, stats.NonMatchingProperties - stats.MatchingProperties * TopShelfConfig.IgnoreNeutral.Value) * TopShelfConfig.NeutralPenalty.Value, TopShelfConfig.MaxNeutralPenalty.Value) * basePayment;
      float hatedPenalty = Math.Max(stats.HatedProperties.Count * TopShelfConfig.HatedPenalty.Value, TopShelfConfig.MaxHatedPenalty.Value) * basePayment;

      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"BonusCalculator.AddAffinityPenalty: Calculated neutralPenalty={neutralPenalty:F2}, hatedPenalty={hatedPenalty:F2}, NonMatchingCalc=({stats.NonMatchingProperties} - {stats.MatchingProperties} * {TopShelfConfig.IgnoreNeutral.Value}) * {TopShelfConfig.NeutralPenalty.Value:F2}={((stats.NonMatchingProperties - stats.MatchingProperties * TopShelfConfig.IgnoreNeutral.Value) * TopShelfConfig.NeutralPenalty.Value):F2}, HatedCalc={stats.HatedProperties.Count} * {TopShelfConfig.HatedPenalty.Value:F2}={stats.HatedProperties.Count * TopShelfConfig.HatedPenalty.Value:F2}");

      if (neutralPenalty != 0)
      {
        bonuses.Add(new BonusPayment("Neutral Penalty", neutralPenalty));
        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg($"BonusCalculator.AddAffinityPenalty: Added Non-Matching Penalty={neutralPenalty:F2}, NonMatching={stats.NonMatchingProperties}, Matching={stats.MatchingProperties}");
      }
      else if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"BonusCalculator.AddAffinityPenalty: No Neutral Penalty applied, calculated value={neutralPenalty:F2}");

      if (hatedPenalty != 0)
      {
        bonuses.Add(new BonusPayment("Hated Penalty", hatedPenalty));
        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg($"BonusCalculator.AddAffinityPenalty: Added Hated Penalty={hatedPenalty:F2}, HatedProperties={stats.HatedProperties.Count}");
      }
      else if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"BonusCalculator.AddAffinityPenalty: No Hated Penalty applied, calculated value={hatedPenalty:F2}");
    }
  }

  [RegisterTypeInIl2Cpp]
  public class AffinityPopup : MonoBehaviour
  {
    private static GameObject popupCanvas;
    public Text AffinityText;
    private CanvasGroup canvasGroup;
    private RectTransform rectTransform;
    private ContentSizeFitter contentSizeFitter;
    private Customer customer;

    private void Awake()
    {
      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"AffinityPopup.Awake");
      canvasGroup = GetComponent<CanvasGroup>();
      rectTransform = GetComponent<RectTransform>();
      contentSizeFitter = GetComponent<ContentSizeFitter>();
      AffinityText = gameObject.transform.Find("AffinityText").GetComponent<Text>();
      canvasGroup.alpha = 1f;
      canvasGroup.blocksRaycasts = false;
      canvasGroup.interactable = false;
    }

    public void Show(Customer customer, Vector2 position)
    {
      this.customer = customer;
      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"AffinityPopup.Show for customer={customer.NPC.fullName}, position={position}");
      UpdateAffinityText();

      Canvas canvas = GetComponentInParent<Canvas>();
      if (canvas == null)
      {
        MelonLogger.Error("AffinityPopup.Show: No Canvas found in parent hierarchy.");
        return;
      }
      Vector2 canvasPos;
      RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas.GetComponent<RectTransform>(), position, canvas.worldCamera, out canvasPos);
      rectTransform.anchoredPosition = canvasPos + new Vector2(10f, -10f) / canvas.scaleFactor;
      LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);

      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"AffinityPopup.Show: anchoredPosition={rectTransform.anchoredPosition}, sizeDelta={rectTransform.sizeDelta}, canvasScale={canvas.scaleFactor}");
    }

    private void UpdateAffinityText()
    {
      string text = customer.NPC.fullName;
      text += "\n\nProduct Affinities";
      var affinities = customer.currentAffinityData.ProductAffinities;
      Color red = new Color(242f / 255f, 18f / 255f, 18f / 255f);         // #f21212
      Color lightRed = new Color(228f / 255f, 115f / 255f, 115f / 255f);  // #e47373
      Color yellow = new Color(242f / 255f, 164f / 255f, 18f / 255f);     //rgb(242, 164, 18)
      Color darkGreen = new Color(0f, 100f / 255f, 0f);                   // #006400
      Color green = new Color(18f / 255f, 242f / 255f, 18f / 255f);       // #12f212
      for (int i = 0; i < affinities.Count; i++)
      {
        float affinity = affinities[i].Affinity;
        Color color;
        float t;
        string range;
        if (affinity < -0.5f)
        {
          range = "[-1, -0.5]";
          t = (affinity + 1f) / 0.5f; // Map [-1, -0.5] to [0, 1]
          color = Color.Lerp(red, lightRed, t);
        }
        else if (affinity < 0f)
        {
          range = "[-0.5, 0]";
          t = (affinity + 0.5f) / 0.5f; // Map [-0.5, 0] to [0, 1]
          color = Color.Lerp(lightRed, yellow, t);
        }
        else if (affinity < 0.5f)
        {
          range = "[0, 0.5]";
          t = affinity / 0.5f; // Map [0, 0.5] to [0, 1]
          color = Color.Lerp(yellow, darkGreen, t);
        }
        else
        {
          range = "[0.5, 1]";
          t = (affinity - 0.5f) / 0.5f; // Map [0.5, 1] to [0, 1]
          color = Color.Lerp(darkGreen, green, t);
        }
        text += $"\n<color=#{ColorUtility.ToHtmlStringRGBA(color)}>•  {affinities[i].DrugType}</color>";
        if (DebugConfig.EnableDebugLogs) MelonLogger.Msg($"AffinityPopup.UpdateAffinityText: Affinity={affinity}, Range={range}, t={t:F3}, Color=({color.r:F3}, {color.g:F3}, {color.b:F3}), Hex=#{ColorUtility.ToHtmlStringRGBA(color)}");
      }

      text += "\n\nFavorite Effects";
      for (int i = 0; i < customer.customerData.PreferredProperties.Count; i++)
      {
        text += "\n<color=#" + ColorUtility.ToHtmlStringRGBA(customer.customerData.PreferredProperties[i].LabelColor) + ">•  " + customer.customerData.PreferredProperties[i].Name + "</color>";
        if (DebugConfig.EnableDebugLogs) MelonLogger.Msg($"AffinityPopup.UpdateAffinityText Favorite color=#{ColorUtility.ToHtmlStringRGBA(customer.customerData.PreferredProperties[i].LabelColor)}, Name={customer.customerData.PreferredProperties[i].Name}");
      }
      text += "\n\nHated Effects";
      if (NegativeProperties.TryGetValue(customer.NPC.GUID, out var properties))
      {
        for (int i = 0; i < properties.Count; i++)
        {
          text += "\n<color=#" + ColorUtility.ToHtmlStringRGBA(properties[i].LabelColor) + ">•  " + properties[i].Name + "</color>";
          if (DebugConfig.EnableDebugLogs) MelonLogger.Msg($"AffinityPopup.UpdateAffinityText: Hated color=#{ColorUtility.ToHtmlStringRGBA(properties[i].LabelColor)}, Name={properties[i].Name}");
        }
      }
      AffinityText.text = text;

      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"AffinityPopup.UpdateAffinityText: Set text with {affinities.Count} product affinities, {customer.customerData.PreferredProperties.Count} preferred, {properties.Count} negative properties.");
    }

    public static GameObject GetPopupPrefab()
    {
      if (popupCanvas == null || popupPrefab == null)
      {
        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg("AffinityPopup.GetPopupPrefab: Canvas or prefab missing, generating.");
        GeneratePrefab();
      }
      return popupPrefab;
    }

    public static void GeneratePrefab()
    {
      if (popupCanvas != null && popupPrefab != null)
      {
        if (DebugConfig.EnableDebugLogs) MelonLogger.Msg("AffinityPopup.GeneratePrefab: Canvas and prefab already exist.");
        return;
      }

      popupCanvas = new GameObject("AffinityPopupCanvas");
      var canvas = popupCanvas.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      canvas.sortingOrder = 1000;
      var scaler = popupCanvas.AddComponent<CanvasScaler>();
      scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
      scaler.referenceResolution = new Vector2(1920, 1080);
      DontDestroyOnLoad(popupCanvas);

      popupPrefab = new GameObject("AffinityPopup");
      popupPrefab.transform.SetParent(popupCanvas.transform, false);
      popupPrefab.AddComponent<CanvasGroup>();
      var rectTransform = popupPrefab.GetComponent<RectTransform>() ?? popupPrefab.AddComponent<RectTransform>();
      rectTransform.sizeDelta = new Vector2(150f, 100f);
      var image = popupPrefab.AddComponent<Image>();
      image.color = new Color(0.1f, 0.1f, 0.1f, 0.99f);
      image.raycastTarget = false;
      var contentSizeFitter = popupPrefab.AddComponent<ContentSizeFitter>();
      contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
      contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
      var layoutGroup = popupPrefab.AddComponent<VerticalLayoutGroup>();
      layoutGroup.padding = new RectOffset(10, 10, 10, 10);
      layoutGroup.spacing = 5f;
      layoutGroup.childAlignment = TextAnchor.UpperLeft;

      var textObj = new GameObject("AffinityText");
      textObj.transform.SetParent(popupPrefab.transform, false);
      var text = textObj.AddComponent<Text>();
      text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
      text.fontSize = 14;
      text.color = Color.white;
      text.alignment = TextAnchor.UpperLeft;
      text.raycastTarget = false;
      text.supportRichText = true;

      var popup = popupPrefab.AddComponent<AffinityPopup>();
      popup.AffinityText = text;

      popupPrefab.SetActive(false);
      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg("AffinityPopup prefab and canvas created and cached.");
    }
  }

  [HarmonyPatch(typeof(Phone))]
  public static class PhonePatch
  {
    [HarmonyPostfix]
    [HarmonyPatch("Update")]
    public static void UpdatePostfix()
    {
      if (GameInput.GetButtonDown(GameInput.ButtonCode.Escape) || GameInput.GetButtonDown(GameInput.ButtonCode.Back))
        if (PopupHolder.popup != null)
        {
          UnityEngine.Object.Destroy(PopupHolder.popup.gameObject);
          PopupHolder.popup = null;
          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Msg("Phone.RequestCloseApp: Destroyed popup on UI close");
        }
    }
  }

  [HarmonyPatch(typeof(DealerManagementApp))]
  public static class DealerManagementAppPatch
  {
    [HarmonyPostfix]
    [HarmonyPatch("SetDisplayedDealer")]
    public static void SetDisplayedDealerPostfix(DealerManagementApp __instance, Dealer dealer)
    {
      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"DealerManagementApp.SetDisplayedDealer for dealer={dealer.fullName}, CustomerEntries.Length={__instance.CustomerEntries.Length}");
      for (int j = 0; j < __instance.CustomerEntries.Length; j++)
      {
        if (dealer.AssignedCustomers.Count > j)
        {
          Customer customer = dealer.AssignedCustomers[j];
          RectTransform entry = __instance.CustomerEntries[j];

          // Add EventTrigger to Mugshot
          Transform mugshotTransform = entry.Find("Mugshot");
          if (mugshotTransform == null)
          {
            MelonLogger.Error($"DealerManagementApp: Mugshot not found for entry {j}, customer={customer.NPC.fullName}");
            continue;
          }
          EventTrigger mugshotTrigger = mugshotTransform.GetComponent<EventTrigger>() ?? mugshotTransform.gameObject.AddComponent<EventTrigger>();
          mugshotTrigger.triggers.Clear();
          var mugshotEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
          mugshotEnter.callback.AddListener(DelegateSupport.ConvertDelegate<UnityAction<BaseEventData>>((BaseEventData data) =>
          {
            // Destroy existing popup
            if (PopupHolder.popup != null)
            {
              UnityEngine.Object.Destroy(PopupHolder.popup.gameObject);
              PopupHolder.popup = null;
              if (DebugConfig.EnableDebugLogs)
                MelonLogger.Msg($"DealerManagementApp: Mugshot PointerEnter destroyed existing popup for customer={customer.NPC.fullName}");
            }
            var prefab = AffinityPopup.GetPopupPrefab();
            if (prefab == null)
            {
              MelonLogger.Error("DealerManagementApp: Mugshot PointerEnter: Popup prefab is null");
              return;
            }
            PopupHolder.popup = UnityEngine.Object.Instantiate(prefab, prefab.transform.parent).GetComponent<AffinityPopup>();
            PopupHolder.popup.gameObject.SetActive(true);
            PopupHolder.popup.Show(customer, Input.mousePosition);
            if (DebugConfig.EnableDebugLogs)
              MelonLogger.Msg($"DealerManagementApp: Mugshot PointerEnter created popup for customer={customer.NPC.fullName}");
          }));
          var mugshotExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
          mugshotExit.callback.AddListener(DelegateSupport.ConvertDelegate<UnityAction<BaseEventData>>((BaseEventData data) =>
          {
            if (PopupHolder.popup != null)
            {
              UnityEngine.Object.Destroy(PopupHolder.popup.gameObject);
              PopupHolder.popup = null;
              if (DebugConfig.EnableDebugLogs)
                MelonLogger.Msg($"DealerManagementApp: Mugshot PointerExit destroyed popup for customer={customer.NPC.fullName}");
            }
          }));
          mugshotTrigger.triggers.Add(mugshotEnter);
          mugshotTrigger.triggers.Add(mugshotExit);

          // Add EventTrigger to Name
          Transform nameTransform = entry.Find("Name");
          if (nameTransform == null)
          {
            MelonLogger.Error($"DealerManagementApp: Name not found for entry {j}, customer={customer.NPC.fullName}");
            continue;
          }
          Text nameText = nameTransform.GetComponent<Text>();
          if (nameText == null)
          {
            MelonLogger.Error($"DealerManagementApp: Name Text component not found for entry {j}, customer={customer.NPC.fullName}");
            continue;
          }
          nameText.raycastTarget = true;
          EventTrigger nameTrigger = nameTransform.GetComponent<EventTrigger>() ?? nameTransform.gameObject.AddComponent<EventTrigger>();
          nameTrigger.triggers.Clear();
          var nameEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
          nameEnter.callback.AddListener(DelegateSupport.ConvertDelegate<UnityAction<BaseEventData>>((BaseEventData data) =>
          {
            // Destroy existing popup
            if (PopupHolder.popup != null)
            {
              UnityEngine.Object.Destroy(PopupHolder.popup.gameObject);
              PopupHolder.popup = null;
              if (DebugConfig.EnableDebugLogs)
                MelonLogger.Msg($"DealerManagementApp: Name PointerEnter destroyed existing popup for customer={customer.NPC.fullName}");
            }
            var prefab = AffinityPopup.GetPopupPrefab();
            if (prefab == null)
            {
              MelonLogger.Error("DealerManagementApp: Name PointerEnter: Popup prefab is null");
              return;
            }
            PopupHolder.popup = UnityEngine.Object.Instantiate(prefab, prefab.transform.parent).GetComponent<AffinityPopup>();
            PopupHolder.popup.gameObject.SetActive(true);
            PopupHolder.popup.Show(customer, Input.mousePosition);
            if (DebugConfig.EnableDebugLogs)
              MelonLogger.Msg($"DealerManagementApp: Name PointerEnter created popup for customer={customer.NPC.fullName}");
          }));
          var nameExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
          nameExit.callback.AddListener(DelegateSupport.ConvertDelegate<UnityAction<BaseEventData>>((BaseEventData data) =>
          {
            if (PopupHolder.popup != null)
            {
              UnityEngine.Object.Destroy(PopupHolder.popup.gameObject);
              PopupHolder.popup = null;
              if (DebugConfig.EnableDebugLogs)
                MelonLogger.Msg($"DealerManagementApp: Name PointerExit destroyed popup for customer={customer.NPC.fullName}");
            }
          }));
          nameTrigger.triggers.Add(nameEnter);
          nameTrigger.triggers.Add(nameExit);
        }
      }
    }
  }

  [HarmonyPatch(typeof(BonusPayment))]
  public static class BonusPaymentPatch
  {
    [HarmonyPostfix]
    [HarmonyPatch(MethodType.Constructor, new Type[] { typeof(string), typeof(float) })]
    public static void BonusPaymentPostfix(BonusPayment __instance, string title, float amount)
    {
      if (__instance == null)
      {
        MelonLogger.Error("BonusPaymentPatch.BonusPaymentPostfix: __instance is null.");
        return;
      }
      try
      {
        __instance.Title = title;
        __instance.Amount = amount;
        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg($"BonusPaymentPatch.BonusPaymentPostfix: Set Title={title}, Amount={amount:F2}");
      }
      catch (Exception e)
      {
        MelonLogger.Error($"BonusPaymentPatch.BonusPaymentPostfix: Failed to set properties for BonusPayment. Error: {e}");
      }
    }
  }

  [HarmonyPatch(typeof(CustomerSelector))]
  public static class CustomerSelectorPatch
  {
    [HarmonyPostfix]
    [HarmonyPatch("CreateEntry")]
    public static void CreateEntryPostfix(CustomerSelector __instance, Customer customer)
    {
      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"CustomerSelector.CreateEntryPostfix for customer={customer.NPC.fullName}, customerEntries.Count={__instance.customerEntries.Count}");
      RectTransform component = __instance.customerEntries[__instance.customerEntries.Count - 1];
      EventTrigger trigger = component.GetComponent<EventTrigger>() ?? component.gameObject.AddComponent<EventTrigger>();
      trigger.triggers.Clear();

      var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
      enter.callback.AddListener(DelegateSupport.ConvertDelegate<UnityAction<BaseEventData>>((BaseEventData data) =>
      {
        // Destroy existing popup
        if (PopupHolder.popup != null)
        {
          UnityEngine.Object.Destroy(PopupHolder.popup.gameObject);
          PopupHolder.popup = null;
          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Msg($"CustomerSelector: PointerEnter destroyed existing popup for customer={customer.NPC.fullName}");
        }
        var prefab = AffinityPopup.GetPopupPrefab();
        if (prefab == null)
        {
          MelonLogger.Error("CustomerSelector: PointerEnter: Popup prefab is null");
          return;
        }
        PopupHolder.popup = UnityEngine.Object.Instantiate(prefab, prefab.transform.parent).GetComponent<AffinityPopup>();
        PopupHolder.popup.gameObject.SetActive(true);
        PopupHolder.popup.Show(customer, Input.mousePosition);
        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg($"CustomerSelector: PointerEnter created popup for customer={customer.NPC.fullName}");
      }));
      trigger.triggers.Add(enter);

      var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
      exit.callback.AddListener(DelegateSupport.ConvertDelegate<UnityAction<BaseEventData>>((BaseEventData data) =>
      {
        if (PopupHolder.popup != null)
        {
          UnityEngine.Object.Destroy(PopupHolder.popup.gameObject);
          PopupHolder.popup = null;
          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Msg($"CustomerSelector: PointerExit destroyed popup for customer={customer.NPC.fullName}");
        }
      }));
      trigger.triggers.Add(exit);

      var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
      click.callback.AddListener(DelegateSupport.ConvertDelegate<UnityAction<BaseEventData>>((BaseEventData data) =>
      {
        if (PopupHolder.popup != null)
        {
          UnityEngine.Object.Destroy(PopupHolder.popup.gameObject);
          PopupHolder.popup = null;
          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Msg($"CustomerSelector: PointerClick destroyed popup for customer={customer.NPC.fullName}");
        }
      }));
      trigger.triggers.Add(click);
    }

    [HarmonyPostfix]
    [HarmonyPatch("Exit")]
    public static void ExitPostfix(ExitAction action)
    {
      if (PopupHolder.popup != null)
      {
        UnityEngine.Object.Destroy(PopupHolder.popup.gameObject);
        PopupHolder.popup = null;
        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg("Phone.RequestCloseApp: Destroyed popup on UI close");
      }
    }
  }

  [HarmonyPatch(typeof(Customer))]
  public static class CustomerPatch
  {
    private const float CurfewBonusMultiplier = 0.2f;
    private const float QuickDeliveryBonusMultiplier = 0.1f;
    private const float GenerosityBonusPerExtra = 10f;
    private const float AddictionChangeFactor = 0.2f;
    private const float AddictionLerpMin = 0.75f;
    private const float AddictionLerpMax = 1.5f;

    /// <summary>
    /// Overrides ProcessHandover to apply TopShelf bonuses for all transactions.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch("ProcessHandover", typeof(HandoverScreen.EHandoverOutcome), typeof(Contract), typeof(Il2CppSystem.Collections.Generic.List<ItemInstance>), typeof(bool), typeof(bool))]
    public static bool ProcessHandoverPrefix(Customer __instance, HandoverScreen.EHandoverOutcome outcome, Contract contract, Il2CppSystem.Collections.Generic.List<ItemInstance> items, bool handoverByPlayer, bool giveBonuses)
    {
      if (DebugConfig.EnableDebugLogs)
      {
        MelonLogger.Msg($"ProcessHandoverPatch.Prefix: customer={__instance?.NPC.fullName ?? "null"}, outcome={outcome}, contractGUID={contract?.GUID.ToString() ?? "null"}, itemCount={items?.Count ?? 0}, handoverByPlayer={handoverByPlayer}, giveBonuses={giveBonuses}");
        if (contract != null)
          MelonLogger.Msg($"ProcessHandoverPatch.Prefix: contract details - Payment={contract.Payment:F2}, ProductListCount={contract.ProductList?.GetTotalQuantity() ?? 0}, QuestState={contract.QuestState}, AcceptTime={contract.AcceptTime.ToString() ?? "null"}");
        if (items != null)
          for (int i = 0; i < items.Count; i++)
            MelonLogger.Msg($"ProcessHandoverPatch.Prefix: Item[{i}]={items[i]?.Definition?.Name ?? "null"}, Type={items[i]?.GetType().FullName ?? "null"}");
      }

      if (__instance == null || items == null)
      {
        MelonLogger.Error($"ProcessHandoverPatch.Prefix: Invalid input: customer={__instance}, items={items}");
        return true;
      }

      var npc = __instance.NPC;
      var relationData = npc.RelationData;

      float highestAddiction;
      EDrugType mainType;
      int matchedProductCount;
      float satisfaction = Mathf.Clamp01(__instance.EvaluateDelivery(contract, items, out highestAddiction, out mainType, out matchedProductCount));
      __instance.ChangeAddiction(highestAddiction / 5f);

      float relationDelta = relationData.RelationDelta;
      float relationshipChange = CustomerSatisfaction.GetRelationshipChange(satisfaction);
      float affinityChange = relationshipChange * AddictionChangeFactor * Mathf.Lerp(AddictionLerpMin, AddictionLerpMax, highestAddiction);
      __instance.AdjustAffinity(mainType, affinityChange);
      relationData.ChangeRelationship(relationshipChange);

      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"ProcessHandoverPatch.Prefix: satisfaction={satisfaction:F2}, highestAddiction={highestAddiction:F2}, mainType={mainType}, matchedProductCount={matchedProductCount}, relationshipChange={relationshipChange:F2}, affinityChange={affinityChange:F2}");

      var bonuses = new Il2CppSystem.Collections.Generic.List<BonusPayment>();
      giveBonuses = true;
      if (giveBonuses)
      {
        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg($"ProcessHandoverPatch.Prefix: Calling AddTopShelfBonus for customer={__instance.NPC.fullName}, handoverByPlayer={handoverByPlayer}");
        BonusCalculator.AddTopShelfBonus(bonuses, __instance, contract, items);

        if (NetworkSingleton<CurfewManager>.Instance.IsCurrentlyActive)
        {
          bonuses.Add(new BonusPayment("Curfew Bonus", contract.Payment * CurfewBonusMultiplier));
          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Msg($"ProcessHandoverPatch.Prefix: Added Curfew Bonus={contract.Payment * CurfewBonusMultiplier:F2}");
        }

        int totalQuantity = contract.ProductList.GetTotalQuantity();
        if (matchedProductCount > totalQuantity)
        {
          float generosityBonus = GenerosityBonusPerExtra * (matchedProductCount - totalQuantity);
          bonuses.Add(new BonusPayment("Generosity Bonus", generosityBonus));
          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Msg($"ProcessHandoverPatch.Prefix: Added Generosity Bonus={generosityBonus:F2}, extraProducts={matchedProductCount - totalQuantity}, totalQuantity={totalQuantity}");
        }

        if (!(contract.QuestState == EQuestState.Inactive) && handoverByPlayer)
        {
          GameDateTime acceptTime = contract.AcceptTime;
          GameDateTime end = new GameDateTime(acceptTime.elapsedDays, TimeManager.AddMinutesTo24HourTime(contract.DeliveryWindow.WindowStartTime, 60));
          if (NetworkSingleton<TimeManager>.Instance.IsCurrentDateWithinRange(acceptTime, end))
          {
            bonuses.Add(new BonusPayment("Quick Delivery Bonus", contract.Payment * QuickDeliveryBonusMultiplier));
            if (DebugConfig.EnableDebugLogs)
              MelonLogger.Msg($"ProcessHandoverPatch.Prefix: Added Quick Delivery Bonus={contract.Payment * QuickDeliveryBonusMultiplier:F2}, acceptTime={acceptTime}, end={end}");
          }
        }
      }

      float totalBonus = 0f;
      foreach (var bonus in bonuses)
      {
        totalBonus += bonus.Amount;
        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg($"ProcessHandoverPatch.Prefix: Bonus: Title={bonus.Title}, Amount={bonus.Amount:F2}");
      }

      if (handoverByPlayer)
      {
        Singleton<HandoverScreen>.Instance.ClearCustomerSlots(returnToOriginals: false);
        contract.SubmitPayment(totalBonus);
        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg($"ProcessHandoverPatch.Prefix: Cleared slots, submitted payment={totalBonus:F2}");

        if (outcome == HandoverScreen.EHandoverOutcome.Finalize)
        {
          Singleton<DealCompletionPopup>.Instance.PlayPopup(__instance, satisfaction, relationDelta, contract.Payment, bonuses);
          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Msg($"ProcessHandoverPatch.Prefix: Played popup with satisfaction={satisfaction:F2}, payment={contract.Payment:F2}, bonuses={bonuses.Count}, totalBonus={totalBonus:F2}");
        }
      }
      else
      {
        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg($"ProcessHandoverPatch.Prefix: Skipped popup for non-player transaction");
      }

      __instance.TimeSinceLastDealCompleted = 0;
      npc.SendAnimationTrigger("GrabItem");

      NetworkObject dealerNetworkObject = contract.Dealer?.NetworkObject;
      float totalPayment = Mathf.Clamp(contract.Payment + totalBonus, 0f, float.MaxValue);

      __instance.ProcessHandoverServerSide(outcome, items, handoverByPlayer, totalPayment, contract.ProductList, satisfaction, dealerNetworkObject);

      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"ProcessHandoverPatch.Prefix: Completed, Base payment={contract.Payment:F2}, Total bonus={totalBonus:F2}, Satisfaction={satisfaction:F2}, totalPayment={totalPayment:F2}, dealer={dealerNetworkObject?.name ?? "null"}");

      return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch("GetSaveString")]
    public static void GetSaveStringPostfix(Customer __instance, ref string __result)
    {
      try
      {
        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg($"Customer.GetSaveStringPostfix: Starting for customer {__instance.NPC.fullName} (GUID: {__instance.NPC.GUID})");

        var json = JObject.Parse(__result);
        if (NegativeProperties.TryGetValue(__instance.NPC.GUID, out var negativeProperties))
        {
          var negativePropertiesData = new JArray();
          foreach (var property in negativeProperties)
          {
            negativePropertiesData.Add(new JObject
            {
              ["PropertyID"] = property.ID
            });
          }
          json["NegativeProperties"] = negativePropertiesData;
          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Msg($"Customer.GetSaveStringPostfix: Added {negativeProperties.Count} NegativeProperties for customer {__instance.NPC.fullName}");
        }

        __result = json.ToString(Il2CppNewtonsoft.Json.Formatting.Indented);

        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg($"Customer.GetSaveStringPostfix: Saved JSON for customer {__instance.NPC.fullName}: {__result}");
      }
      catch (Exception e)
      {
        MelonLogger.Error($"Customer.GetSaveStringPostfix: Failed for customer {__instance.NPC.fullName}, error: {e}");
      }
    }
  }

  [HarmonyPatch(typeof(DealCompletionPopup))]
  public static class DealCompletionPopupPatch
  {
    [HarmonyPrefix]
    [HarmonyPatch("PlayPopup")]
    public static bool PlayPopupPrefix(DealCompletionPopup __instance, Customer customer, float satisfaction, float originalRelationshipDelta, float basePayment, Il2CppSystem.Collections.Generic.List<BonusPayment> bonuses)
    {
      __instance.IsPlaying = true;
      if (__instance.routine != null)
      {
        __instance.StopCoroutine(__instance.routine);
      }

      __instance.routine = (Coroutine)MelonCoroutines.Start(Routine());
      // Skip the original method
      return false;

      System.Collections.IEnumerator Routine()
      {
        // Set IsPlaying to true
        __instance.IsPlaying = true;

        // Initialize UI
        __instance.Group.alpha = 0f;
        __instance.Canvas.enabled = true;
        __instance.Container.gameObject.SetActive(value: true);
        __instance.Title.text = "Deal completed for " + customer.NPC.fullName;
        __instance.PaymentLabel.text = "+$0";
        __instance.SatisfactionValueLabel.text = "0%";
        __instance.SatisfactionValueLabel.color = __instance.SatisfactionGradient.Evaluate(0f);
        for (int i = 0; i < __instance.BonusLabels.Length; i++)
        {
          if (bonuses.Count > i)
          {
            // Handle negative bonuses correctly
            string sign = bonuses[i].Amount >= 0 ? "+" : "-";
            string color = bonuses[i].Amount >= 0 ? "#54E717" : "#f21212"; // #54E717 Green for positive, #f21212 red for negative
            __instance.BonusLabels[i].text = $"<color={color}>{sign}{MoneyManager.FormatAmount(Mathf.Abs(bonuses[i].Amount))}</color> {bonuses[i].Title}";
            __instance.BonusLabels[i].gameObject.SetActive(true);
          }
          else
          {
            __instance.BonusLabels[i].gameObject.SetActive(false);
          }
        }

        yield return new WaitForSeconds(0.2f);
        __instance.Anim.Play();
        __instance.SoundEffect.Play();
        __instance.RelationCircle.AssignNPC(customer.NPC);
        __instance.RelationCircle.SetUnlocked(NPCRelationData.EUnlockType.Recommendation, false);
        __instance.RelationCircle.SetNotchPosition(originalRelationshipDelta);
        __instance.SetRelationshipLabel(originalRelationshipDelta);
        yield return new WaitForSeconds(0.2f);
        float paymentLerpTime = 1.5f;
        for (float i2 = 0f; i2 < paymentLerpTime; i2 += Time.deltaTime)
        {
          __instance.PaymentLabel.text = "+" + MoneyManager.FormatAmount(basePayment * (i2 / paymentLerpTime));
          yield return new WaitForEndOfFrame();
        }
        __instance.PaymentLabel.text = "+" + MoneyManager.FormatAmount(basePayment);
        yield return new WaitForSeconds(1.5f);
        float satisfactionLerpTime = 1f;
        for (float i2 = 0f; i2 < satisfactionLerpTime; i2 += Time.deltaTime)
        {
          __instance.SatisfactionValueLabel.color = __instance.SatisfactionGradient.Evaluate(i2 / satisfactionLerpTime * satisfaction);
          __instance.SatisfactionValueLabel.text = Mathf.Lerp(0f, satisfaction, i2 / satisfactionLerpTime).ToString("P0");
          yield return new WaitForEndOfFrame();
        }

        __instance.SatisfactionValueLabel.color = __instance.SatisfactionGradient.Evaluate(satisfaction);
        __instance.SatisfactionValueLabel.text = satisfaction.ToString("P0");
        yield return new WaitForSeconds(0.25f);
        float endDelta = customer.NPC.RelationData.RelationDelta;
        float lerpTime = Mathf.Abs(customer.NPC.RelationData.RelationDelta - originalRelationshipDelta);
        for (float i2 = 0f; i2 < lerpTime; i2 += Time.deltaTime)
        {
          float num = Mathf.Lerp(originalRelationshipDelta, endDelta, i2 / lerpTime);
          __instance.RelationCircle.SetNotchPosition(num);
          __instance.SetRelationshipLabel(num);
          yield return new WaitForEndOfFrame();
        }

        __instance.RelationCircle.SetNotchPosition(endDelta);
        __instance.SetRelationshipLabel(endDelta);
        yield return new WaitUntil(DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(() => __instance.Group.alpha == 0f));
        __instance.Canvas.enabled = false;
        __instance.Container.gameObject.SetActive(false);
        __instance.IsPlaying = false;

        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg($"DealCompletionPopupPatch: Completed popup for customer {customer.NPC.fullName} with doubled post-fade-in/pre-fade-out times.");
      }
    }
  }

  [HarmonyPatch(typeof(NPCLoader))]
  public static class NPCLoaderPatch
  {
    [HarmonyPostfix]
    [HarmonyPatch("Load")]
    public static void LoadPostfix(NPCLoader __instance, string mainPath)
    {
      try
      {
        string text;
        if (__instance.TryLoadFile(mainPath, "NPC", out text))
        {
          NPCData data = JsonUtility.FromJson<NPCData>(text);
          NPC npc = NPCManager.NPCRegistry._items.FirstOrDefault(x => x.ID == data.ID);
          if (npc == null) return;
          Customer customer = npc.GetComponent<Customer>();
          if (customer == null) return;

          if (!__instance.TryLoadFile(mainPath, "CustomerData", out text)) return;
          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Warning($"NPCLoader.LoadPostfix: NPC {npc.fullName} json={text}");
          var jsonObject = JObject.Parse(text);
          if (jsonObject["NegativeProperties"] == null)
          {
            // No saved data, initialize new NegativeProperties
            InitializeNegativeProperties(customer);
            if (DebugConfig.EnableDebugLogs)
              MelonLogger.Msg($"NPCLoader.LoadPostfix: Initialized NegativeProperties for customer {customer.NPC.fullName} (GUID: {customer.NPC.GUID})");
          }
          else if (jsonObject["NegativeProperties"].TryCast<JArray>() is JArray negativePropertiesData)
          {
            if (DebugConfig.EnableDebugLogs)
              MelonLogger.Warning($"NPCLoader.LoadPostfix: NPC {npc.fullName} found jsonObject[NegativeProperties]={jsonObject["NegativeProperties"]}");
            var negativeProperties = new List<Property>();
            var allProperties = Resources.LoadAll<Property>("Properties");
            for (int i = 0; i < negativePropertiesData.Count; i++)
            {
              var propData = negativePropertiesData[i];
              string propID = propData["PropertyID"]?.ToString();
              if (!string.IsNullOrEmpty(propID))
              {
                var property = allProperties.FirstOrDefault(p => p.ID == propID);
                if (property != null)
                  negativeProperties.Add(property);
                else
                  if (DebugConfig.EnableDebugLogs)
                  MelonLogger.Warning($"NPCLoader.LoadPostfix: Failed to load Property with ID: {propID} for NPC {npc.fullName}");
              }
            }
            NegativeProperties[customer.NPC.GUID] = negativeProperties;
            if (DebugConfig.EnableDebugLogs)
              MelonLogger.Msg($"NPCLoader.LoadPostfix: Loaded {negativeProperties.Count} NegativeProperties for customer {customer.NPC.fullName} (GUID: {customer.NPC.GUID})");
            return;
          }
        }
      }
      catch (Exception e)
      {
        MelonLogger.Error($"NPCLoader.LoadPostfix: Failed for {mainPath}, error: {e}");
      }
    }
  }

  [HarmonyPatch(typeof(ContactsDetailPanel))]
  public static class ContactsDetailPanelPatch
  {
    [HarmonyPrefix]
    [HarmonyPatch("Open")]
    public static bool OpenPrefix(ContactsDetailPanel __instance, NPC npc)
    {
      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Warning($"Customer.OpenPrefix for customer={npc.fullName}");
      __instance.PropertiesLabel.alignment = TextAnchor.UpperLeft;
      return true;
    }

    [HarmonyPostfix]
    [HarmonyPatch("Open")]
    public static void OpenPostfix(ContactsDetailPanel __instance, NPC npc)
    {
      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Warning($"Customer.OpenPostfix for customer={npc.fullName}");
      Customer component = npc.GetComponent<Customer>();
      if (component != null)
      {
        __instance.PropertiesLabel.text += "\n\nHated Effects";
        if (NegativeProperties.TryGetValue(npc.GUID, out var properties))
          for (int i = 0; i < properties.Count; i++)
          {
            __instance.PropertiesLabel.text += $"\n<color=#{ColorUtility.ToHtmlStringRGBA(properties[i].LabelColor)}>•  {properties[i].Name}</color>";
          }
      }
    }
  }
}