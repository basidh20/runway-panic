# Airport Bird Control: Runway Panic

Third-person 3D shooting game (Unity) — SE3032 Graphics & Visualization × SE3062 Intelligent Systems group project, SLIIT.
You play an airport wildlife-control officer defending the runway from 4 species of AI-controlled birds across 4 timed waves.

## Getting started

1. Install **Git LFS** once per machine, then clone:
   ```bash
   git lfs install
   git clone https://github.com/basidh20/runway-panic.git
   ```
2. Set your Git identity to the email on your **GitHub account** (commits are graded individually):
   ```bash
   git config user.name  "Your Name"
   git config user.email "your-github-email@example.com"
   ```
3. Install **Unity `6000.6.4f1`** (Unity 6.6) in Unity Hub — exactly this version, not a newer/older patch.
4. Open the repo root folder in **Unity Hub → Add → Add project from disk**. If Hub warns about a version mismatch, pick `6000.6.4f1` — do **not** upgrade.

**Render pipeline:** URP 17.6.0. URP assets and input actions live in `Assets/_Project/Settings/`.

## Folder structure

```
runway-panic/
├── Assets/
│   ├── _Project/            ← all our own work goes here
│   │   ├── Art/             Models, Materials, Textures, Animations, Shaders
│   │   ├── Audio/           SFX, Music
│   │   ├── Prefabs/         Player, Birds, Weapons, Interactables, Environment, UI
│   │   ├── Scenes/          Main.unity (World Builder only), Sandbox/ (one per member)
│   │   ├── Scripts/         Core, Player, Interaction, Weapons, Agents, AI, UI
│   │   ├── ScriptableObjects/  Settings/  UI/
│   └── ThirdParty/          ← Asset Store / external packages
├── SourceArt/               ← .blend files, source textures, references (not imported by Unity)
└── Docs/                    ← design docs, images, viva notes
```

## Conventions

- **Naming:** `SM_` static mesh · `SK_` skinned mesh · `M_` material · `T_` texture · `P_` prefab · `A_` animation · `AC_` animator controller (e.g. `SK_Player_Officer`, `SM_Gun_Tier1`).
- **Scenes:** only the World Builder edits `Main.unity`. Everyone else works in `Scenes/Sandbox/Sandbox_<Name>.unity` and delivers **prefabs**.
- **Binary files** (`.blend`, `.fbx`, `.png`) have one owner each — they cannot be merged.
- **Always commit the `.meta` file** together with its asset.
- **Branches:** `feature/<role>-<task>` (e.g. `feature/core-gun-modular`), `fix/<task>`. Merge to `main` via Pull Request.
- **Commits:** Conventional Commits — `<type>(<area>): <message>`
  - types: `feat | fix | refactor | perf | docs | chore | art`
  - areas: `models | level | lighting | navmesh | physics | player | agents | ai | ui`
  - e.g. `art(models): add tier 1 modular gun`
- Do **not** commit builds or the demo video.

## Team roles (GV)

| Role | Member |
|---|---|
| World Builder | _TBD_ |
| Systems Engineer | _TBD_ |
| Core Developer (3D models) | Basidh Nizam |
| Agent Controller | _TBD_ |
