using System.Threading;
using Content.Shared.Cargo;
using Robust.Shared.GameStates;
using Content.Shared._NF.Bank.Components;

namespace Content.Shared._NF.Trade;

/// <summary>
/// This is used to add an additional tax on an item when sold
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedCargoSystem))]
public sealed partial class ItemTaxComponent : Component
{
    [DataField(serverOnly: true)]
    public Dictionary<SectorBankAccount, float> TaxAccounts = new();
}
