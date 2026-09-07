# Review sources

Files here build managed human-decision packages. They are development sources, never runtime mod assets.

`small_flywheel.py` owns only `generated/small-flywheel-review/current/`. It reproduces the four exact 3×3×1 candidates used to choose the completed Small Flywheel. E4 (`model-e4-iron-eight-way-frame`) is the approved runtime design. Omit `--approved` to compare the archived candidates, or pass that candidate ID to reproduce the closed decision package.

The current set keeps E1 through E3 and adds E4: eight straight stripped-oak arms enclosed by a large octagonal iron socket frame, with narrow iron straps continuing along the unbent arms.

`slingshot_workflow.py` owns only `generated/slingshot-review/current/`. Each run replaces that fixed directory, so repeated review rounds do not create new revision trees or orphaned scripts. The generated ownership manifest records the source, candidates, and decision.

`overrunning_coupling.py` owns only `generated/overrunning-coupling-review/current/`. Its locking-interface round keeps the approved A2 frame, four wooden input supports, and three-arm orbit while comparing three complete tooth-and-pawl pairs: a fifteen-position smooth brass ramp with a restored long hook, a twelve-pocket reinforced-oak ring with thin brass glide plates and a broad drop bar, and a nine-point swept wheel with the concept-art crook. Option A now uses one narrow bronze contact stick truncated at the former club shoulder, carries its visible follower cap with that stick, overlaps the oak rim and thin brass tire to hide sectional gaps, and gives the diagnostic tooth pass four explicit recovery frames after its frame-23 peak. The circled clevis side plates are removed, and every flat locking shoulder meets its pawl on an exact shared plane. Full-assembly, isolated-interface, isolated-input, and isolated-output states remain review-only until the maintainer approves one pair.

`lateral_motion_system.py` owns only `generated/lateral-motion-review/current/`. A7 remains the animated double-acting reference, A9 the unframed one-block baseline, and A10 the accepted two-block bottom-fed design. A11 removes A10's lower service block while retaining the three-unit crank throw, six-unit stroke, eight-unit piston, glass chamber, and centered side-face connections at y=-16. It does not enter the chamber through those side walls: each 4-by-4 line turns into a flush shared-wall drop, runs inward at y=-21.25, then turns up through its own uncapped 3-by-3 reservoir-floor port. The slightly recessed inlet check lifts into the chamber on suction without entering the piston envelope, and the outlet check drops into its riser on pressure. A low oak cradle stand now appears only in A11's supported downward-facing pump-bottom, cutaway, and pipe-fit states; side- and upward-facing states show the unframed pump. Broad runners, four short legs, split saddles, horizontal corner struts, stout knees, and local iron plates transfer the vessel load into the block below without surrounding the chamber as a permanent cage. The exact `pipe-fit` state places complete horizontal neighbor blocks at x=-16 and x=16. All three single-acting candidates retain paired output-side air checks, animation, a true one-sided crank, and no visible example liquid. The checks are now vertical and seated directly in two openings on the output half of the split pressure ceiling: the atmospheric intake is nearly flush and opens downward into the chamber, while the exhaust has a short exposed cage and opens upward. The rejected side branches are gone, the output chamber wall is solid, and the complete terminal rim of the main output coupling is bronze, supplying a second restrained direction cue without changing pipe alignment. The recessed rod well keeps its flush bronze stuffing plate; narrowed guides, the inward valve cage, and the piston remain clear through the full stroke. They remain approval-only, and the automatic large bellows is shelved.

The normal command reproduces the approved C result. Pass `--open` only to restart the three-candidate workflow test. Approval applies to the fixture, not to a runtime slingshot.

From the repository root:

```powershell
$env:PYTHONPATH = "tools/graphics"
python graphics/review/small_flywheel.py --root .
python -m gearwright_graphics.review_model --review generated/small-flywheel-review/current

python graphics/review/slingshot_workflow.py --root .
.\tools\graphics\Review-Model.ps1 -PythonPath python -ReviewPath generated/slingshot-review/current

python graphics/review/overrunning_coupling.py --root .
.\tools\graphics\Review-Model.ps1 -PythonPath python -ReviewPath generated/overrunning-coupling-review/current

python graphics/review/lateral_motion_system.py --root .
.\tools\graphics\Review-Model.ps1 -PythonPath python -ReviewPath generated/lateral-motion-review/current
```

The slingshot is a test fixture for reviewing models and animations. Do not copy it into `graphics/models/`, `assets/gearwright/`, or gameplay code.
