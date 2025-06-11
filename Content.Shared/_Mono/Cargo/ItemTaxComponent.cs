using Content.Shared._NF.Bank.Components;
using Content.Shared.Cargo;
using Robust.Shared.GameStates;

namespace Content.Shared._Mono.ItemTax.Components;

/// <summary>
/// This is used to add additional money to a budget when a specific item is sold
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedCargoSystem))]
public sealed partial class ItemTaxComponent : Component
{
    [DataField]
    public Dictionary<SectorBankAccount, float> TaxAccounts = new();
}
