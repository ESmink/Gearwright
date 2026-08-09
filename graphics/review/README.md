# Review-only workflow fixtures

Files here exercise the human model-approval workflow. They are development sources, never runtime mod assets.

`slingshot_workflow.py` owns only `generated/slingshot-review/current/`. Each run replaces that fixed directory, so repeated review rounds do not create new revision trees or orphaned scripts. The generated ownership manifest records the source, candidates, and decision.

The normal command reproduces the approved C result. Pass `--open` only to restart the three-candidate workflow test. Approval applies to the fixture, not to a runtime slingshot.

From the repository root:

```powershell
$env:PYTHONPATH = "tools/graphics"
python graphics/review/slingshot_workflow.py --root .
.\tools\graphics\Review-Model.ps1 -PythonPath python -ReviewPath generated/slingshot-review/current
```

The slingshot is a test fixture for reviewing models and animations. Do not copy it into `graphics/models/`, `assets/gearwright/`, or gameplay code.
