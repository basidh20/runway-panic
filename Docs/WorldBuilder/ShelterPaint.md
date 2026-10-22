# Changing shelter exterior colours

The two shelters have independent materials in
`Assets/_Project/Art/Materials/WorldSurfaces/`:

- `M_Shelter_A_Exterior_FadedBlue`: faded dark blue, initial tint `#546E88`.
- `M_Shelter_B_Exterior_LightBrickRed`: very light brick red, initial tint `#DDB0A5`.

## Manual colour changes

1. Stop Play mode. In the Project window search for the material name above.
2. Select the material asset, not the shelter prefab or a texture.
3. In the Inspector expand **Surface Inputs**. Click the colour swatch beside
   **Base Map** (not the texture thumbnail).
4. Choose a colour or enter its hex value. Keep alpha at 255/FF (fully opaque).
5. Close the colour picker and choose **File > Save Project**.
6. Check the result in Main, then briefly in Play mode. Warm afternoon lighting
   and the concrete texture affect its appearance, so it won't look like a
   perfectly flat colour sample.

No script editing or rerunning a tool is needed to change the colour. Changing
one material recolours that shelter's exterior wall faces only. Do not edit
`M_Building_Concrete`, the shader or texture slots just to change paint colour;
those are shared with other surfaces.

## Implementation

`Tools > Runway Panic > Paint Shelter Exterior Walls` creates the initial setup
in saved Main outside Play mode. It preserves colours on existing paint assets
when rerun. Six wall renderers per shelter use two material slots: original
concrete for the interior/edges and a shelter-specific material for the outward
face. Four shared cube mesh variants split triangle/material assignments only;
vertices, UV/UV2, wall dimensions and colliders are unchanged. Roofs, door
frames, doors, floor markings and the source shelter prefab are not repainted.

No new NavMesh bake is needed for unchanged collision. The existing indirect
lighting bake is retained; this is a material-colour change, not a newly baked
lighting result. Preview and owner-review results should be recorded below.

## Review

Applied to all 12 exterior wall faces in Main and saved through the editor.
Separate editor-camera previews confirmed the blue and brick-red front faces
after the renderer refresh. The concrete texture remains visible; the shaded
front of Shelter B looks darker than its light paint swatch. Scene changes are
limited to the 12 wall mesh/material assignments. No gameplay walkthrough or
new lighting bake was performed; owner visual approval is still pending.

## Corner flicker repair

The original 7.6m side walls overlapped the front/back walls by 0.2m at each
end. Their outward faces were coplanar with the concrete end faces of the
front/back walls, producing z-fighting at all four corners of each shelter.
`Tools > Runway Panic > Fix Shelter Corner Overlap` fits the side walls between
the front/back inner faces (7.2m) in saved Main, with Undo support. The four
side-wall BoxColliders follow their transforms; only the redundant overlap is
removed, so the combined solid building envelope, doors and usable interior
remain unchanged. The source prefab and paint colours are untouched.

Applied and saved for both Main shelter instances. Oblique editor previews of
both shelters show clean corner joins without the jagged overlap. Owner should
briefly walk around both buildings in Play mode to confirm motion flicker is
gone; a still preview does not verify temporal behaviour.
