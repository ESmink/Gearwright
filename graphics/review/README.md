# Review sources

Files here build managed human-decision packages. They are development sources, never runtime mod assets.

`small_flywheel.py` owns only `generated/small-flywheel-review/current/`. It reproduces the four exact 3×3×1 candidates used to choose the completed Small Flywheel. E4 (`model-e4-iron-eight-way-frame`) is the approved runtime design. Omit `--approved` to compare the archived candidates, or pass that candidate ID to reproduce the closed decision package.

The current set keeps E1 through E3 and adds E4: eight straight stripped-oak arms enclosed by a large octagonal iron socket frame, with narrow iron straps continuing along the unbent arms.

`slingshot_workflow.py` owns only `generated/slingshot-review/current/`. Each run replaces that fixed directory, so repeated review rounds do not create new revision trees or orphaned scripts. The generated ownership manifest records the source, candidates, and decision.

The normal command reproduces the approved C result. Pass `--open` only to restart the three-candidate workflow test. Approval applies to the fixture, not to a runtime slingshot.

From the repository root:

```powershell
$env:PYTHONPATH = "tools/graphics"
python graphics/review/small_flywheel.py --root .
python -m gearwright_graphics.review_model --review generated/small-flywheel-review/current

python graphics/review/slingshot_workflow.py --root .
.\tools\graphics\Review-Model.ps1 -PythonPath python -ReviewPath generated/slingshot-review/current
```

The slingshot is a test fixture for reviewing models and animations. Do not copy it into `graphics/models/`, `assets/gearwright/`, or gameplay code.
