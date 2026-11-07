# Shield Expanded 1.7 port

This context covers the port of Shield Expanded, a 1.6 mod by FoxMo, to RimWorld 1.7. The port lives in this repo beside the 1.6 build and never changes that build.

## Mod and build

**Shield Expanded**:
The 1.6 mod by FoxMo, Steam Workshop item 3575073524. The port is its 1.7 version.
_Avoid_: RimElShrine (that name also covers the repo, the core library, and two other mods)

**Port**:
The project that makes Shield Expanded run on RimWorld 1.7. It produces the 1.7 build.
_Avoid_: Fork

**1.6 build**:
The existing 1.6 version of Shield Expanded, in `RimElShrine.Shields/1.6/`.

**1.7 build**:
The 1.7 version of Shield Expanded, in `RimElShrine.Shields/1.7/`.

**Core library**:
The code that the RimElShrine mods share, in `Source/RimElShrine/`.

## Shield content

**Content group**:
One of the three groups that the Workshop description lists: Shield Buildings & Shield Network, Shield Equipment, and Mech Serums. The mod settings are not a content group, because they apply to all three.

**Shield network**:
The Kinetic Shield Generators, Shield Control Terminals, and Shield Server Units that the power grid connects. Upgrades that a Shield Control Terminal allocates apply to every shield in the network.

**Kinetic Shield Generator**:
A constructible shield. Its energy loss on a hit depends on the projectile's kinetic energy or damage value.

**Shield Control Terminal**:
Controls how upgrade resources are allocated across a shield network. With no terminal in the network, no upgrades apply, and the Kinetic Shield Generators recover energy slowly.

**Shield Server Unit**:
Supplies upgrade resources that a Shield Control Terminal can allocate.

**ES shield belt**:
A wearable shield belt from the ES series, ES0 to ES3. Advanced ES0 to ES2 models absorb environmental damage. The ES3 belt recharges its shield faster than a vanilla shield belt, at the cost of minor bloodloss in the wearer.

**Mech Serum**:
A serum made by one of the mech serum recipes. The mod adds the Empty Mech Serum, an intermediate that other serums are made from, and the Regenerator series, which gradually repairs damaged limbs.

**Fast injector**:
A Regenerator injector with the Fast setting. It turns on quick regen, so it helps only a pawn with blood loss. See ADR 0011.

**Quick regen**:
The faster regen mode. It costs blood loss, so it runs only while the pawn has blood loss.

## Compatibility and proof

**Combat Extended path**:
The `1.6/Compatible/CombatExtended/` folder and the `RimElShrine.Shields.CE.dll` assembly in it. RimWorld loads the folder only while `CETeam.CombatExtended` is active.
_Avoid_: CE mode

**Runtime validation run**:
A launch of RimWorld that runs test scenarios inside the game and records each result as PASS or FAIL. It uses its own save folder, so existing saves and the live mod list are not touched. Only one RimWorld session runs at a time, because every session writes the same game log. See ADR 0010. The Z-Levels runner in `rimworld-modding/tools/` is one example.
