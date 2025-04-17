using MelonLoader;
using HarmonyLib;
using ScheduleOne.Economy;
using ScheduleOne.ItemFramework;
using ScheduleOne.Product;
using ScheduleOne.Quests;
using ScheduleOne.UI.Handover;
using System.Reflection;
using System.Reflection.Emit;

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
        {
          MelonLogger.Msg("Harmony patches applied for CustomerProcessHandoverPatch.");
          LogHarmonyPatches(harmony);
        }
      }
      catch (Exception ex)
      {
        MelonLogger.Error($"Failed to apply Harmony patches: {ex.Message}\n{ex.StackTrace}");
      }
    }

    private static void LogHarmonyPatches(HarmonyLib.Harmony harmony)
    {
      var patchedMethods = harmony.GetPatchedMethods();
      foreach (var method in patchedMethods)
      {
        MelonLogger.Msg($"Patched method: {method.DeclaringType.FullName}.{method.Name}");
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

      // Inject after list initialization (IL_0041)
      for (int i = 0; i < codes.Count - 1; i++)
      {
        if (codes[i].opcode == OpCodes.Newobj && codes[i].operand is ConstructorInfo ci && ci.DeclaringType == typeof(List<Contract.BonusPayment>) &&
            codes[i + 1].opcode == OpCodes.Stloc_S)
        {
          insertIndex = i + 2;
          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Msg($"Found list initialization at IL_{i:D4}, inserting at IL_{insertIndex:D4}");
          break;
        }
      }

      if (insertIndex == -1)
      {
        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Error("Transpiler failed: Could not find List<Contract.BonusPayment> initialization. Dumping operand types for newobj:");
        for (int i = 0; i < codes.Count; i++)
        {
          if (codes[i].opcode == OpCodes.Newobj)
            MelonLogger.Msg($"IL_{i:D4}: newobj operand = {codes[i].operand?.GetType().Name}, {codes[i].operand}");
        }
        return codes;
      }

      // Find local index of 'list'
      var listIndex = codes.FirstOrDefault(c => c.opcode == OpCodes.Stloc_S && c.operand is LocalBuilder lb && lb.LocalType == typeof(List<Contract.BonusPayment>))?.operand as LocalBuilder;

      if (listIndex == null)
      {
        MelonLogger.Error("Transpiler failed: Could not determine list local index for List<Contract.BonusPayment>.");
        return codes;
      }

      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg($"Found list local index: {listIndex.LocalIndex}");

      // Log nearby branches
      if (DebugConfig.EnableDebugLogs && insertIndex > 0)
      {
        MelonLogger.Msg("Checking for branches before insertion point:");
        for (int i = Math.Max(0, insertIndex - 10); i < insertIndex; i++)
        {
          if (codes[i].opcode == OpCodes.Br || codes[i].opcode == OpCodes.Brfalse || codes[i].opcode == OpCodes.Brtrue)
          {
            MelonLogger.Msg($"Branch at IL_{i:D4}: {codes[i].opcode} to {codes[i].operand}");
          }
        }
      }

      // Instructions to call AddTopShelfBonus
      var newInstructions = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldloc_S, listIndex),       // Load list
                new CodeInstruction(OpCodes.Ldarg_0),                  // Load this (Customer)
                new CodeInstruction(OpCodes.Ldarg_2),                  // Load contract
                new CodeInstruction(OpCodes.Ldarg_3),                  // Load items
                //new CodeInstruction(OpCodes.Ldarg_S, (byte)5),         // Load giveBonuses
                new CodeInstruction(OpCodes.Call, typeof(CustomerProcessHandoverPatch).GetMethod(nameof(AddTopShelfBonus)))
            };

      // Insert instructions
      codes.InsertRange(insertIndex, newInstructions);

      if (DebugConfig.EnableDebugLogs)
        MelonLogger.Msg("Transpiler successfully patched ProcessHandover with TopShelfBonus logic.");

      return codes;
    }

    public static void AddTopShelfBonus(List<Contract.BonusPayment> list, Customer customer, Contract contract, List<ItemInstance> items)
    {
      try
      {
        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg($"AddTopShelfBonus invoked: {customer != null} {list != null} {customer?.customerData?.Standards != null} {contract != null}");

        if (customer == null || list == null || customer?.customerData?.Standards == null)
        {
          MelonLogger.Error("AddQualityBonus skipped: Invalid parameters.");
          return;
        }

        // Log existing bonuses
        if (DebugConfig.EnableDebugLogs)
        {
          MelonLogger.Msg($"Existing bonuses in list (Count={list.Count}):");
          foreach (var bonus in list)
          {
            MelonLogger.Msg($"  Bonus: {bonus.Title}, Amount={bonus.Amount}");
          }
        }

        // Check for existing Quality Bonus
        if (list.Any(b => b.Title == "Quality Bonus"))
        {
          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Warning("AddTopShelfBonus: Quality Bonus already exists in list, skipping.");
          return;
        }

        int requiredQuality = (int)StandardsMethod.GetCorrespondingQuality(customer.customerData.Standards);
        int totalExcessLevels = 0;
        int totalItems = 0;

        if (items == null || items.Count == 0)
        {
          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Warning("AddTopShelfBonus: items list is null or empty.");
        }
        else
        {
          foreach (var item in items)
          {
            if (item == null)
            {
              if (DebugConfig.EnableDebugLogs)
                MelonLogger.Warning("AddTopShelfBonus: item is null.");
              continue;
            }

            if (item is ProductItemInstance productItem)
            {
              int itemQuality = (int)productItem.Quality;
              totalItems++;
              if (DebugConfig.EnableDebugLogs)
                MelonLogger.Msg($"Item {item.ID}: Quality={itemQuality}, Required={requiredQuality}");
              if (itemQuality > requiredQuality)
              {
                totalExcessLevels += (itemQuality - requiredQuality);
              }
            }
            else if (DebugConfig.EnableDebugLogs)
            {
              MelonLogger.Warning($"ItemInstance {item.ID} is not a ProductItemInstance. Type: {item.GetType().Name}");
            }
          }
        }

        if (DebugConfig.EnableDebugLogs)
          MelonLogger.Msg($"Calculated totalExcessLevels={totalExcessLevels}, bonusAmount={(totalExcessLevels > 0 ? totalExcessLevels * 0.05f * contract.Payment : 0)}, totalItems={totalItems}");

        if (totalExcessLevels > 0)
        {
          float bonusAmount = totalExcessLevels * 0.05f * contract.Payment;
          if (DebugConfig.EnableDebugLogs)
            MelonLogger.Msg($"Creating BonusPayment: Title=Quality Bonus, Amount={bonusAmount}");
          try
          {
            var bonusPayment = new Contract.BonusPayment("Quality Bonus", bonusAmount);
            list.Add(bonusPayment);
            if (DebugConfig.EnableDebugLogs)
              MelonLogger.Msg($"Quality Bonus added: Amount={bonusAmount}, Average Quality={(float)totalExcessLevels / totalItems:F2}, Required Quality={requiredQuality}, Total Items={totalItems}");
          }
          catch (Exception ex)
          {
            MelonLogger.Error($"BonusPayment creation failed: {ex.Message}\n{ex.StackTrace}");
            return;
          }
        }
        else if (DebugConfig.EnableDebugLogs)
        {
          MelonLogger.Msg($"No Quality Bonus added: totalExcessLevels={totalExcessLevels}, Required Quality={requiredQuality}, Total Items={totalItems}");
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
      {
        string callId = Guid.NewGuid().ToString().Substring(0, 8);
        MelonLogger.Msg($"ProcessHandover Prefix [CallID={callId}]: outcome={outcome}, giveBonuses={giveBonuses}, handoverByPlayer={handoverByPlayer}");
      }
      return true;
    }
  }
}