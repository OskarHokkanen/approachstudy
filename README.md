# Approach Study prototype


## What the prototype does

A virtual character walks towards a participant who wears a Meta Quest 3 or 3S in passthrough, so they see their real room. The app records how the participant moves while it approaches. You decide how each character behaves: how far away it starts, which direction it comes from, how far to the side it passes, how fast it walks, how big it is, which model it uses and where it stops.

The prototype uses a cube as the character. You can swap in any 3D model, including animated ones (see *Extending the prototype*).


The prototype is built on Unity 6's Mixed Reality template. All the study logic lives in one folder, `Assets/ApproachStudy`.

## How a session works

One session = one participant. You calibrate once, then send the characters one by one in the chosen order. The app records every trial and saves everything when the last one is done.

&#91;embedded content: session flow · 4 states, 1 decision\]

An aborted trial goes back to *Ready* and runs again, so every trial in the order ends up with one complete recording.

## Project structure

Everything you'll normally change is in **one asset**, the Experiment Config. You only need to open the scripts to add new behaviour.

| File / object | What it is | Do you edit it? |
| --- | --- | --- |
| `Configs/ExampleStudy.asset` | **Experiment Config**: the list of trials, the trial orders and session settings. You can make one per study | Yes, this is where you design the study |
| `Approach Study` (scene object) | Holds the **Experiment Controller**. It points to the config and has passthrough, floor, display and button settings | Sometimes (to switch config or display options) |
| `Scripts/ExperimentController.cs` | Runs the session: buttons, calibration, trial order, starting and ending trials, keeping passthrough on | Only to change the procedure |
| `Scripts/ExperimentConfig.cs` | Defines what a config contains and builds the trial orders (shuffle, Latin square, custom) | Only to add settings |
| `Scripts/TrialDefinition.cs` | Defines the settings for one trial (distance, speed, offset…) | Only to add trial settings |
| `Scripts/ApproachCharacter.cs` | Creates the cube or your model, and walks it along the path | Only to change how the character moves |
| `Scripts/MotionLogger.cs` | Writes the CSV files and calculates the summary measures | Only to add new measures |
| `Scripts/StatusDisplay.cs` | The floating text panel for the person running the session | Rarely |
| `Editor/ApproachStudySetup.cs` | The **Tools > Approach Study** menu (add to scene, create materials, open data folder) | No |
| `Materials/` | URP materials for the cube, floor marker and panel. Without them, objects show up pink | No |
| `Analysis/plot_session.py` | Python script that plots the recorded data (outside Unity) | Optional |

The template's own objects stay in the scene too: **MR Interaction Setup** holds the camera, controllers, AR Session and room-scan planes, so don't delete it. The template's tutorial UI (Goal Manager, Coaching UI) is switched off.

## Designing trials

A trial is one kind of approach. To edit trials, select your config in the Project window and open **Trials** in the Inspector. Hover over a field to see what it does. Each trial needs a unique `id`, a short name without commas such as `near_fast`; it's used in orders and in the data.

All positions are measured from the **calibrated spot** (where the participant stood when you pressed A) and the direction they faced at that moment. Distances are in metres, speed in metres per second and angles in degrees.

| Setting | What it controls | Example |
| --- | --- | --- |
| `startDistance` | How far from the spot the character appears | `4` |
| `approachAngle` | Direction it comes from: 0 = straight ahead, + = from the participant's right, – = from their left | `30` |
| `lateralOffset` | Shifts the whole path sideways: 0 = walks straight at the participant, + = passes on their right, – = on their left | `0.5` |
| `speed` | Walking speed. Normal human walking is about 1.2–1.4 m/s | `1.2` |
| `startDelay` | Seconds it stands still before it starts walking | `1` |
| `stopDistance` | Where it stops, measured back from the participant's end of the path | `1` |
| `endBehaviour` | **Stop Then Vanish**, **Stop And Stay** (stays until the next trial) or **Walk Past Then Vanish** | – |
| `holdTime` | Seconds it stays after stopping | `2` |
| `walkPastDistance` | For walk-past trials: how far behind the participant it keeps walking | `2` |
| `characterPrefab` | Empty = cube, or drag in a 3D model | – |
| `cubeColor`, `cubeSize` | Cube colour and size (width, height, depth in m) | `0.45, 1.75, 0.3` |
| `scale` | Size multiplier for the cube or model | `1.25` = 25 % bigger |
| `modelPositionOffset` | Moves the visible model away from its path (x = the character's right, z = its forward). Use it for models whose pivot isn't centred, or to offset the body on purpose | `0.1, 0, 0` |
| `modelRotationOffset` | Rotates the model, e.g. `0, 180, 0` if it faces backwards | – |

**To make a character walk past the participant:** set `endBehaviour` to *Walk Past Then Vanish* and set a non-zero `lateralOffset`, otherwise it walks straight through them. Walk-past trials ignore `stopDistance` and `holdTime`.

**Check the room.** The character doesn't avoid furniture, and passthrough doesn't hide it behind real objects. Make sure there's free space of `startDistance` in front of the spot, plus `walkPastDistance` behind it for walk-past trials.

**Settings are built into the app.** After changing trials, rebuild with Build And Run.

## Trial orders

Each participant can get a different order without rebuilding the app. Choose the order in the headset during setup by moving the right thumbstick left and right. The config's **Default Order Mode** sets which one is selected at start-up.

| Order | What it does | Use it when |
| --- | --- | --- |
| **As listed** | Runs the trials in list order, repeated `repetitions` times | Piloting and testing |
| **Shuffled** | Random order; each repetition is shuffled separately. The seed is saved in `session.txt`, so you can reproduce the order | You want randomisation and don't need perfect balance |
| **Latin square** | A balanced Latin square row picked from the participant number. Across participants, each trial appears equally often in every position and follows every other trial equally often | Counterbalancing order effects. You need a multiple of the number of trials in participants (or twice that number when it's odd) |
| **Custom** | An order you write yourself under **Custom Orders**, e.g. `baseline, fast, offset_right, baseline`. You can use ids or trial numbers (`3, 1, 2`), and repeats are allowed | A planned order per participant or per group |

Tips:

- Write one custom order per participant (name them `P1`, `P2`…) and select the matching one in the headset.
- Set `shuffleSeed` to a fixed number if everyone should get the same shuffled order. Leave it at 0 for a new random order each session.
- The setup panel shows the exact trial sequence before you start, so check it there.

## Running a session in the headset

The participant wears the headset; you, the experimenter, hold the controllers. Cast the headset view to a phone or laptop (Meta Horizon app) so you can read the status panel the participant sees.

| Button | Setup | Between trials | During a trial | Session finished |
| --- | --- | --- | --- | --- |
| **A** (right) | Calibrate and start | Send the next character | – | Set up the next participant |
| **B** (right) | – | Press twice to end the session early | Abort and redo this trial | – |
| **X** (left) | – | Recalibrate the spot | – | – |
| **Y** (left) | Show or hide the status panel | same | same | same |
| **Right stick ← / →** | Choose the trial order | – | – | – |
| **Right stick ↑ / ↓** | Change the participant number | – | – | – |

In the Unity Editor the keyboard does the same: Space = A, Backspace = B, C = X, H = Y, and the arrow keys = the stick.

**Before the participant arrives**

1. Do the room scan once per room: on the Quest, **Settings > Physical Space > Space Setup**.
2. Start the app and allow spatial data if asked.
3. Check the order and participant number on the panel.

**With the participant**

1. Ask them to stand on their spot and look straight ahead. Press **A**. A green disc with an arrow marks the calibrated spot and direction on the floor.
2. Press **A** for each character. The panel hides itself while the character walks.
3. If something disturbs a trial (talking, lost tracking, a mistake), press **B**. It's logged as aborted and runs again as attempt 2.
4. If the participant recentres the headset (holding the Meta button), the panel warns you. Press **X** to recalibrate before the next trial.
5. After the last trial the panel shows where the data was saved. Press **A** to set up the next participant; the number goes up by one.

Keep your instructions to participants identical every time, e.g. *"Stand on the green mark, look ahead and react naturally."* What you tell them affects how they move.

## The data

Every session gets its own folder on the headset, named after the participant and the start time:

```
Android/data/<package name>/files/ApproachStudy/P003_2026-11-20_10-15-02/
```

To copy it, connect the Quest to a computer with USB and allow file access in the headset. On Windows, browse to *Quest 3 › Internal shared storage › Android › data*. On Mac, use Android File Transfer or `adb pull`. Runs in the Unity Editor are saved on your computer; open them with **Tools > Approach Study > Open Data Folder**.

| File | Contents |
| --- | --- |
| `trials.csv` | **One row per trial**: every setting plus the summary measures below. Start your analysis here |
| `samples.csv` | One row per frame (72–120 per second): head position and rotation, character position and distance |
| `events.csv` | Time-stamped log of calibrations, trial start and end, aborts, tracking resets and app pauses |
| `session.txt` | Participant, order, seed, every trial's settings, device |

**How movement is measured.** All values are horizontal and in metres.

- **retreat** = movement along the character's approach line since the trial started. Positive = away from the character, negative = towards it.
- **side** = movement sideways since the trial started. Positive = to the participant's right when facing the character.
- **rel\_right / rel\_forward** = where the head is relative to the calibrated spot, in the direction the participant faced at calibration.
- **head\_to\_char** = distance from the head to the centre of the character.

**Summary measures in `trials.csv`**

| Column | Meaning |
| --- | --- |
| `max_retreat_m` | Furthest the participant moved away from the character |
| `max_advance_m` | Furthest they moved towards it |
| `max_side_right_m`, `max_side_left_m` | Largest side step each way |
| `max_disp_from_trial_start_m`, `t_max_disp_s` | Largest movement in any direction, and when it happened |
| `max_disp_from_calib_m` | Largest distance from the calibrated spot |
| `final_retreat_m`, `final_side_m` | Where they ended up at the end of the recording |
| `min_head_to_char_m`, `t_min_head_to_char_s` | Closest the character came, and when |
| `max_abs_yaw_change_deg` | Largest head turn (left/right) during the trial |
| `start_rel_right_m`, `start_rel_forward_m` | How far from the spot they stood when the trial began |
| `aborted`, `attempt` | 1 = aborted trial; attempt counts redos |

Recording starts when the character appears and continues for `postTrialRecordTime` seconds (default 1.5) after it finishes, to capture late reactions.

## Extending the prototype

**Use your own character model**

1. Import the model into Unity (e.g. an `.fbx` with its textures) and drag it into the scene once.
2. Make sure it faces **+Z (blue arrow)**, has its pivot **at the feet** and is about the right height in metres. Then drag it into the Project window to make a prefab.
3. Make sure its materials use a URP shader (*Universal Render Pipeline/Lit*), otherwise it shows up pink in the build.
4. In a trial, drag the prefab into `characterPrefab`. Use `modelRotationOffset` if it walks backwards, `modelPositionOffset` if it's off-centre and `scale` to resize it.

**Make it walk with animation.** Give the prefab an Animator with a walk/idle animation and a **float parameter named `Speed`**. The app sets `Speed` to the walking speed while it walks and to 0 while it stands. In the Animator, make a transition to walking when Speed > 0.1.

**Add a new trial setting.** Add a public field to `TrialDefinition.cs` (it then appears in the Inspector) and use it in `ApproachCharacter.cs`. To record it in the data, add it to the trial row in `MotionLogger.EndTrial` and to the header in `BeginSession`, in the same position in both.

**Add a new measure.** In `MotionLogger.cs`, calculate it in `Sample()` (it runs every frame and has the head position, rotation and character position), store the result in a field, and write it out in `EndTrial()` like the existing measures.

**Change the procedure.** The session flow lives in `ExperimentController.cs`: `HandleInput()` decides what each button does in each state, `StartTrial()` places the character, and `CompleteTrial()` decides what happens next.

Keep a copy of the working project before larger changes, ideally in Git, so you can always go back.

## Troubleshooting

| Problem | Fix |
| --- | --- |
| Black background, no passthrough | Passthrough only works in a **build on the headset**, not in Play mode or over Quest Link. Check that **Force Passthrough** is on in `Approach Study`. In *Project Settings > XR Plug-in Management > OpenXR* (Android tab), **Meta Quest: Camera (Passthrough)** must be ticked |
| Passthrough only after accepting the template popup | Disable the **Goal Manager** component on MR Interaction Setup and the **Coaching UI** object. Never disable MR Interaction Setup itself |
| Pink objects | Run **Tools > Approach Study > Create & Assign Materials**, save the scene and rebuild. Your own models need URP materials |
| Status text missing or pink | *Window > TextMeshPro > Import TMP Essential Resources* |
| Character floats or sinks | Run Space Setup again, or adjust `floorHeightAdjust` on `Approach Study`. The panel shows which floor it used |
| Character starts inside a wall | Lower `startDistance`, or calibrate facing the longest free direction of the room |
| Thumbstick moves or turns the view | Disable the *Locomotion* object under the XR Origin |
| "Tracking was reset" warning | The participant recentred the headset. Press **X** to recalibrate |
| Changed trials don't show up | The config is built into the app, so rebuild |

**Good practice**

- Pilot every new config on yourself first and look at the plots before running real participants.
- Write down the participant number, config and order for each session, and keep `session.txt` with the data.
- Recalibrate if the participant has moved away from the spot between trials (watch `start_rel_*`).
- Keep the room the same between participants: same spot, same lighting, nothing new on the floor.
