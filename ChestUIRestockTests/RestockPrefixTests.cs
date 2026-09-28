using System;
using System.Collections;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using Testaria;

namespace ChestUIRestockTests;

/// <summary>
/// Covers the #StackablePrefixWeapons change to <see cref="ChestUI.Restock"/>.
/// <para/>
/// Vanilla's Restock skipped any chest item carrying a prefix outright
/// (<c>item[i].prefix != 0</c>) before reaching <see cref="Item.CanStack"/>.
/// That made the prefix decision unreachable for exactly the items tML's
/// stackable-prefix support exists to serve. Removing the guard leaves
/// <see cref="Item.CanStack"/> as the sole arbiter, and it requires the two
/// prefixes to be *equal* rather than absent.
/// <para/>
/// These run on the server, which is fine: Restock touches no rendering, and
/// every SoundEngine.PlaySound overload short-circuits on Main.dedServ.
/// <para/>
/// Restock reads <c>Main.player[Main.myPlayer]</c>, which on a server is the
/// dummy in slot 255. The tests borrow that dummy rather than repointing
/// Main.myPlayer at a spawned player: the autosave path also reads
/// Main.myPlayer, and aiming it at a fabricated player crashes the server in
/// Player.InternalSavePlayer. Inventory and bank contents are snapshotted and
/// put back at teardown.
/// <para/>
/// The container is the piggy bank (<c>player.chest == -2</c>), which needs no
/// world chest and no tile placement: Restock reads <c>player.bank.item</c>
/// directly for that value.
/// </summary>
public class RestockPrefixTests
{
	private const int StackableType = ItemID.Torch;
	private const byte PrefixA = PrefixID.Keen;
	private const byte PrefixB = PrefixID.Superior;

	/// <summary>
	/// Hands back the server's dummy player with an empty inventory and piggy
	/// bank, restoring both whatever the test does.
	/// </summary>
	private static Player Subject(TestContext box)
	{
		Player player = Main.player[Main.myPlayer];

		Item[] savedInventory = (Item[])player.inventory.Clone();
		Item[] savedBank = (Item[])player.bank.item.Clone();

		box.Restore(() => {
			Array.Copy(savedInventory, player.inventory, savedInventory.Length);
			Array.Copy(savedBank, player.bank.item, savedBank.Length);
		});

		box.Change(() => player.chest, v => player.chest = v, -2);

		for (int i = 0; i < player.inventory.Length; i++)
			player.inventory[i] = new Item();

		for (int i = 0; i < player.bank.item.Length; i++)
			player.bank.item[i] = new Item();

		return player;
	}

	private static Item Stack(int type, int stack, byte prefix)
	{
		var item = new Item();

		item.SetDefaults(type);
		item.stack = stack;
		item.prefix = prefix;

		return item;
	}

	/// <summary>
	/// The control. Restock has always merged unprefixed stacks, so a failure
	/// here means the harness is wrong rather than the change.
	/// </summary>
	[GameTest(Band = Band.Cavern, Timeout = 600)]
	public IEnumerator Restock_merges_unprefixed_stacks(ITestContext ctx)
	{
		var box = (TestContext)ctx;
		Player player = Subject(box);

		player.inventory[0] = Stack(StackableType, 10, 0);
		player.bank.item[0] = Stack(StackableType, 5, 0);

		ChestUI.Restock();

		yield return Wait.Ticks(1);

		Assert.Equal(15, player.inventory[0].stack, "the bank stack should have merged into the inventory");
		Assert.True(player.bank.item[0].IsAir, "the bank slot should be empty afterwards");
	}

	/// <summary>
	/// The regression this change fixes: before it, the prefixed bank stack was
	/// discarded by <c>item[i].prefix != 0</c> before Item.CanStack could agree
	/// the two were compatible, so the inventory stack stayed at 10.
	/// </summary>
	[GameTest(Band = Band.Cavern, Timeout = 600)]
	public IEnumerator Restock_merges_stacks_sharing_a_prefix(ITestContext ctx)
	{
		var box = (TestContext)ctx;
		Player player = Subject(box);

		player.inventory[0] = Stack(StackableType, 10, PrefixA);
		player.bank.item[0] = Stack(StackableType, 5, PrefixA);

		ChestUI.Restock();

		yield return Wait.Ticks(1);

		Assert.Equal(15, player.inventory[0].stack, "stacks sharing a prefix should merge");
		Assert.True(player.bank.item[0].IsAir, "the bank slot should be empty afterwards");
	}

	/// <summary>
	/// Diagnostic: isolates which stage of Restock rejects the prefixed pair.
	/// Restock only reaches ItemLoader.StackItems if the type is in its hashSet
	/// and Item.CanStack agrees, so assert those directly.
	/// </summary>
	[GameTest(Band = Band.Cavern, Timeout = 600)]
	public IEnumerator Diagnostic_prefixed_pair_should_be_stackable(ITestContext ctx)
	{
		var box = (TestContext)ctx;
		Player player = Subject(box);

		Item inv = Stack(StackableType, 10, PrefixA);
		Item bank = Stack(StackableType, 5, PrefixA);

		player.inventory[0] = inv;
		player.bank.item[0] = bank;

		Assert.Equal((int)PrefixA, (int)inv.prefix, "inventory prefix survived assignment");
		Assert.Equal((int)PrefixA, (int)bank.prefix, "bank prefix survived assignment");
		Assert.True(inv.maxStack > 1, $"inventory maxStack should exceed 1, was {inv.maxStack}");
		Assert.False(inv.favorited, "inventory item must not be favorited");
		Assert.True(ItemLoader.CanStack(inv, bank), "ItemLoader.CanStack should allow a matching prefix");
		Assert.True(Item.CanStack(inv, bank), "Item.CanStack should allow a matching prefix");
		Assert.Equal(0, ItemSlot.PickItemMovementAction(player.inventory, 0, 0, bank), "PickItemMovementAction should permit slot 0");

		yield return Wait.Ticks(1);
	}

	/// <summary>
	/// The guard. Removing the prefix check must not make prefixes irrelevant:
	/// Item.CanStack requires them to be equal, not merely present. Without
	/// this, a change that deleted the check entirely would pass the test above
	/// and silently merge a Keen stack into a Superior one.
	/// </summary>
	[GameTest(Band = Band.Cavern, Timeout = 600)]
	public IEnumerator Restock_leaves_stacks_with_different_prefixes_alone(ITestContext ctx)
	{
		var box = (TestContext)ctx;
		Player player = Subject(box);

		player.inventory[0] = Stack(StackableType, 10, PrefixA);
		player.bank.item[0] = Stack(StackableType, 5, PrefixB);

		ChestUI.Restock();

		yield return Wait.Ticks(1);

		Assert.Equal(10, player.inventory[0].stack, "a different prefix must not merge");
		Assert.Equal(5, player.bank.item[0].stack, "the bank stack should be untouched");
	}
}
