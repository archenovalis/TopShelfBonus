using MelonLoader;
using HarmonyLib;
using ScheduleOne.Economy;
using ScheduleOne.ItemFramework;
using ScheduleOne.Product;
using ScheduleOne.Quests;
using ScheduleOne.UI.Handover;
using System.Reflection.Emit;
using System.Reflection;

[assembly: MelonInfo(typeof(TopShelfBonus.TopShelfBonus), "TopShelfBonus-Mono", "1.0.0", "Archie", "Adds 5% bonus for delivering items with quality above the customer's required standards.")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: HarmonyDontPatchAll]
namespace TopShelfBonus
{
  public static class DebugConfig
  {
    public static bool EnableDebugLogs = false;
  }

  public class TopShelfBonus : MelonMod
  {
    public override void OnInitializeMelon()
    {
      MelonLogger.Msg("TopShelfBonus-Mono initialized.");
      try
      {
        var harmony = new HarmonyLib.Harmony("com.Archie.TopShelfBonus");
        harmony.PatchAll(typeof(CustomerProcessHandoverPatch));
        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg("Harmony patches applied for CustomerProcessHandoverPatch.");
      }
      catch (Exception ex)
      {
        MelonLogger.Error($"Failed to apply Harmony patches: {ex.Message}\n{ex.StackTrace}");
      }
    }
  }

  [HarmonyPatch(typeof(Customer), "ProcessHandover", new[] { typeof(HandoverScreen.EHandoverOutcome), typeof(Contract), typeof(List<ItemInstance>), typeof(bool), typeof(bool) })]
  public static class CustomerProcessHandoverPatch
  {
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg("Transpiler started for Customer.ProcessHandover.");

      var codes = new List<CodeInstruction>(instructions);
      int insertIndex = -1;

      // Log IL instructions for debugging
      if (DebugConfig.EnableDebugLogs)
      {
        MelonLogger.Msg("Dumping IL instructions:");
        for (int i = 0; i < codes.Count; i++)
        {
          MelonLogger.Msg($"IL_{i:D4}: {codes[i].opcode} {codes[i].operand}");
        }
      }

      // Find float num5 = 0f (ldc.r4 0.0 followed by stloc.s)
      for (int i = 0; i < codes.Count - 1; i++)
      {
        if (codes[i].opcode == OpCodes.Newobj && codes[i].operand is ConstructorInfo ci && ci.DeclaringType == typeof(List<Contract.BonusPayment>) &&
            codes[i + 1].opcode == OpCodes.Stloc_S)
        {
          insertIndex = i + 2; // Insert at IL_0043
          break;
        }
      }

      if (insertIndex == -1)
      {
        MelonLogger.Error("Transpiler failed: Could not find List<Contract.BonusPayment> initialization.");
        return codes;
      }

      // Find local index of 'list'
      var listIndex = codes.FirstOrDefault(c => c.opcode == OpCodes.Stloc_S && c.operand is LocalBuilder lb && lb.LocalType == typeof(List<Contract.BonusPayment>))?.operand as LocalBuilder;

      if (listIndex == null)
      {
        MelonLogger.Error("Transpiler failed: Could not determine list local index.");
        return codes;
      }

      // Instructions to call AddTopShelfBonus
      var newInstructions = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldloc_S, listIndex),       // Load list
                new CodeInstruction(OpCodes.Ldarg_0),                  // Load this (Customer)
                new CodeInstruction(OpCodes.Ldarg_2),                  // Load contract
                new CodeInstruction(OpCodes.Ldarg_3),                  // Load items
                new CodeInstruction(OpCodes.Ldarg_S, (byte)5),         // Load giveBonuses
                new CodeInstruction(OpCodes.Call, typeof(CustomerProcessHandoverPatch).GetMethod(nameof(AddTopShelfBonus)))
            };

      codes.InsertRange(insertIndex, newInstructions);
      return codes;
    }

    public static void AddTopShelfBonus(List<Contract.BonusPayment> list, Customer customer, Contract contract, List<ItemInstance> items, bool giveBonuses)
    {
      try
      {
        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg($"AddTopShelfBonus invoked: giveBonuses={giveBonuses}, items.Count={(items != null ? items.Count : 0)}");

        if (!giveBonuses)
        {
          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Msg("AddTopShelfBonus: giveBonuses is false, skipping.");
          return;
        }

        if (customer == null || customer.customerData == null)
        {
          MelonLogger.Error("AddTopShelfBonus skipped: Invalid parameters.");
          return;
        }

        int requiredQuality = (int)StandardsMethod.GetCorrespondingQuality(customer.customerData.Standards);
        if (items == null || items.Count == 0)
        {
          MelonLogger.Warning("AddTopShelfBonus: No items provided.");
          return;
        }

        // Calculate weighted average quality
        var itemData = items
            .Where(item => item is ProductItemInstance)
            .Select(item => (ProductItem: (ProductItemInstance)item, Quality: (int)((ProductItemInstance)item).Quality, Quantity: ((ProductItemInstance)item).Quantity))
            .Where(data => data.Quantity > 0)
            .ToList();

        if (!itemData.Any())
        {
          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Warning("AddTopShelfBonus: No valid ProductItemInstance with positive quantity found.");
          return;
        }

        float totalQuality = itemData.Sum(data => data.Quality * data.Quantity);
        int totalQuantity = itemData.Sum(data => data.Quantity);
        float averageQuality = totalQuality / totalQuantity;
        float totalExcessLevels = averageQuality - requiredQuality;

        if (totalExcessLevels > 0)
        {
          float bonusAmount = totalExcessLevels * 0.05f * contract.Payment;
          try
          {
            var bonusPayment = new Contract.BonusPayment("Quality Bonus", bonusAmount);
            list.Add(bonusPayment);

            if (DebugConfig.EnableDebugLogs)
              MelonLogger.Msg($"Quality Bonus added: Amount={bonusAmount:F2}, Average Quality={averageQuality:F2}, Required Quality={requiredQuality}, Total Items={totalQuantity}");
          }
          catch (Exception ex)
          {
            MelonLogger.Error($"Failed to create BonusPayment: {ex.Message}");
          }
        }
      }
      catch (Exception ex)
      {
        MelonLogger.Error($"AddTopShelfBonus failed: {ex.Message}");
      }
    }

    [HarmonyPrefix]
    public static bool Prefix(HandoverScreen.EHandoverOutcome outcome, bool giveBonuses, bool handoverByPlayer)
    {

      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"ProcessHandover: outcome={outcome}, giveBonuses={giveBonuses}, handoverByPlayer={handoverByPlayer}");
      return true;
    }
  }
}