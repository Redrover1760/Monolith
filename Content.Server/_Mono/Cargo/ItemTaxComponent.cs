using System.Threading;
using Content.Shared.Cargo;
using Robust.Shared.GameStates;
using Content.Shared._NF.Bank.Components;

namespace Content.Server._Mono.ItemTax;

/// <summary>
/// This is used to add an additional tax on an item when sold
/// </summary>
[RegisterComponent]
public sealed partial class ItemTaxComponent : Component
{
    [DataField]
    public Dictionary<SectorBankAccount, float> TaxAccounts = new();
}
