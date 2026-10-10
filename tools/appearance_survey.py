"""Appearance survey: capture every character-creation appearance option on the LIVE doll.

Run with the game open in character creation on the Appearance step (dev server up). Drives the
phase's DollState through /eval (race, gender, head, hair, ...), zooms the doll-room camera, grabs
/screenshot, crops the doll, and lays the crops out as labelled contact sheets for authoring the
descriptions in assets/locale/enGB/appearance.json (desc.<asset name>).

    python tools/appearance_survey.py heads Human Female      # one kind for one race+gender
    python tools/appearance_survey.py hair Human Male
    python tools/appearance_survey.py paints                   # race-independent lists (scars/warpaints/tattoos)
    python tools/appearance_survey.py restore                  # put the doll back as the player had it

Output: tools/out/appearance/<kind>/<asset>.png + <kind>_<race>_<gender>_sheetN.png
"""
import io, json, os, sys, time, urllib.request
from PIL import Image, ImageDraw

DEV = "http://127.0.0.1:8771"
OUT = os.path.join(os.path.dirname(__file__), "out", "appearance")
SHOT = r"C:\Users\bradj\AppData\Local\Temp\wa_shot.png"

PRELUDE = r'''
var asm = typeof(WrathAccess.Settings.ModSettings).Assembly;
var bf = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
var cgs = asm.GetType("WrathAccess.Screens.CharGenScreen");
var cgvm = (Kingmaker.UI.MVVM._VM.CharGen.CharGenVM)cgs.GetMethod("Vm", bf).Invoke(null, null);
var vm = (Kingmaker.UI.MVVM._VM.CharGen.Phases.Appearance.CharGenAppearancePhaseVM)cgvm.CurrentPhaseVM.Value;
var doll = vm.DollState;
var races = Kingmaker.Blueprints.Root.BlueprintRoot.Instance.Progression.CharacterRaces;
var cam = Kingmaker.UI.ServiceWindow.DollCamera.Current;
var camT = typeof(Kingmaker.UI.ServiceWindow.DollCamera);
var raceList = races.Where(r => r != null).ToList();
System.Func<Kingmaker.Blueprints.CharGen.CustomizationOptions> opts = () => doll.Gender == Kingmaker.Blueprints.Gender.Male ? doll.Race.MaleOptions : doll.Race.FemaleOptions;
'''

def ev(code):
    body = (PRELUDE + code).encode("utf-8")
    req = urllib.request.Request(DEV + "/eval", data=body, method="POST")
    with urllib.request.urlopen(req, timeout=60) as r:
        out = r.read().decode("utf-8", "replace")
    if out.startswith("[compile]") or out.startswith("[exception]"):
        raise SystemExit("eval failed: " + out[:600])
    return out

def shot():
    with urllib.request.urlopen(DEV + "/screenshot", timeout=30) as r:
        path = r.read().decode().strip()
    return Image.open(path if path else SHOT)

def zoom(normalized, min_zoom=None):
    code = '''
var presetField = camT.GetField("m_CharacterZoomPreset", bf);
var preset = (Kingmaker.UI.ServiceWindow.DollRoomCameraZoomPreset)presetField.GetValue(cam);
if (preset != null) preset.CanZoom = true;
camT.GetField("m_SmoothZoom", bf).SetValue(cam, 0f);
'''
    if min_zoom is not None:
        code += 'camT.GetField("m_MinZoom", bf).SetValue(cam, %ff);\n' % min_zoom
    code += 'cam.ZoomNormalized = %ff; "zoom " + cam.ZoomNormalized' % normalized
    return ev(code)

def rotate(deg):
    return ev('''
var ctrl = typeof(Kingmaker.UI.ServiceWindow.DollRoom).GetField("m_CharacterController", bf).GetValue(Kingmaker.Game.Instance.UI.Common.DollRoom) as Kingmaker.UI.ServiceWindow.DollRoomCharacterController;
if (ctrl != null && ctrl.Character != null) ctrl.Character.rotation = UnityEngine.Quaternion.Euler(0f, %ff, 0f);
"rotated"''' % deg)

# Crops as fractions of the 4K frame. The doll stands centred; zoomed in, the head sits around (0.47, 0.30).
CROPS = {
    "face": (0.40, 0.13, 0.55, 0.45),
    "head": (0.36, 0.12, 0.59, 0.50),
    "upper": (0.30, 0.10, 0.65, 0.70),
    "body": (0.33, 0.08, 0.62, 0.95),
}

def head_screen():
    """The head bone's position as screen fractions (x from left, y from top), or None."""
    r = ev('''
var avatar = Kingmaker.Game.Instance.UI.Common.DollRoom.GetAvatar();
UnityEngine.Transform target = null;
if (avatar != null) foreach (var t in avatar.GetComponentsInChildren<UnityEngine.Transform>(true)) if (t.name == "Head" && t.parent != null && t.parent.name == "Neck") { target = t; break; }
var c = cam.GetComponent<UnityEngine.Camera>();
var sp = (target != null && c != null) ? c.WorldToScreenPoint(target.position) : UnityEngine.Vector3.zero;
(target == null || c == null) ? "none" : (sp.x / c.pixelWidth).ToString("0.000") + "," + (1f - sp.y / c.pixelHeight).ToString("0.000")''')
    r = r.replace("=> ", "").strip()
    if r == "none": return None
    x, y = r.split(",")
    return float(x), float(y)

# Half-widths / heights (as frame fractions) of the box around the head point, per crop kind.
BOXES = {"face": (0.058, 0.07, 0.11), "head": (0.085, 0.12, 0.14), "closeface": (0.075, 0.09, 0.13), "upper": (0.13, 0.10, 0.34)}

def settled_head():
    """Wait for the doll rebuild to finish: the head's screen position sane and stable across two reads."""
    last = None
    for _ in range(25):
        hp = head_screen()
        if hp is not None and 0.3 < hp[0] < 0.7 and 0.05 < hp[1] < 0.8 and last is not None                 and abs(hp[0] - last[0]) < 0.003 and abs(hp[1] - last[1]) < 0.003:
            return hp
        last = hp
        time.sleep(0.15)
    return last

def capture(name, kind, crop, settle=0.4):
    time.sleep(settle)
    hp = settled_head() if crop in BOXES else None
    im = shot()
    w, h = im.size
    if hp is not None:
        hx, hy, dy = BOXES[crop]
        x0, y0, x1, y1 = hp[0] - hx, hp[1] - hy, hp[0] + hx, hp[1] + dy
    else:
        x0, y0, x1, y1 = CROPS[crop]
    c = im.crop((int(w * x0), int(h * y0), int(w * x1), int(h * y1)))
    d = os.path.join(OUT, kind)
    os.makedirs(d, exist_ok=True)
    p = os.path.join(d, name + ".png")
    c.save(p)
    return p

def sheet(paths, labels, out, cell=(420, 520), cols=4):
    rows = (len(paths) + cols - 1) // cols
    im = Image.new("RGB", (cols * cell[0], rows * (cell[1] + 28)), (20, 20, 20))
    dr = ImageDraw.Draw(im)
    for i, (p, l) in enumerate(zip(paths, labels)):
        c = Image.open(p)
        c.thumbnail(cell)
        x = (i % cols) * cell[0]
        y = (i // cols) * (cell[1] + 28)
        im.paste(c, (x + (cell[0] - c.size[0]) // 2, y))
        dr.text((x + 4, y + cell[1] + 6), l, fill=(255, 255, 255))
    im.save(out)
    return out

def set_race_gender(race, gender):
    return ev('''
doll.SetRace(raceList.First(r => r.Name.ToString() == "%s"));
doll.SetGender(Kingmaker.Blueprints.Gender.%s);
var o = opts();
if (o.Heads.Length > 0) doll.SetHead(o.Heads[0]);
if (o.Hair.Length > 0) { var hs = o.Hair.ToList(); var bald = hs.FirstOrDefault(h => h.Load() != null && (h.Load().name.Contains("Bald") || h.Load().name.Contains("None") || h.Load().name.Contains("EMPTY"))) ?? hs.FirstOrDefault(h => h.Load() != null && h.Load().name.Contains("Short")) ?? hs.Last(); doll.SetHair(bald); }
doll.SetScar(doll.Scars.Count > 0 ? doll.Scars[0] : default(Kingmaker.ResourceLinks.EquipmentEntityLink));
if (o.Beards.Length > 0) doll.SetBeard(o.Beards.FirstOrDefault(b => b.Load() != null && b.Load().name.Contains("EMPTY")) ?? o.Beards.Last());
if (o.Horns.Length > 0) doll.SetHorn(o.Horns.FirstOrDefault(b => b.Load() != null && b.Load().name.Contains("EMPTY")) ?? o.Horns.Last());
if (o.Heads.Length > 0) doll.SetHead(o.Heads[0]);
if (doll.Race.Presets.Length > 0) doll.SetRacePreset(doll.Race.Presets[0]);
doll.SetSkinColor(0);
"race " + doll.Race.Name + " " + doll.Gender + " heads=" + o.Heads.Length + " hair=" + o.Hair.Length + " beards=" + o.Beards.Length + " horns=" + o.Horns.Length''' % (race, gender))

def names(kind):
    """Asset names for a kind on the current doll (same order as the game's selector)."""
    src = {
        "heads": "opts().Heads", "hair": "opts().Hair", "beards": "opts().Beards", "horns": "opts().Horns",
        "scars": "doll.Scars", "warpaints": "doll.Warprints[0].Paints", "tattoos": "doll.Tattoos[0].Paints",
    }[kind]
    r = ev('string.Join("|", %s.Select(e => e.Load() != null ? e.Load().name : "null").ToArray())' % src)
    return r.replace("=> ", "").strip().split("|")

def set_option(kind, i):
    src = {
        "heads": "doll.SetHead(opts().Heads[%d]);", "hair": "doll.SetHair(opts().Hair[%d]);",
        "beards": "doll.SetBeard(opts().Beards[%d]);", "horns": "doll.SetHorn(opts().Horns[%d]);",
        "scars": "doll.SetScar(doll.Scars[%d]);",
        "warpaints": "doll.SetWarpaint(doll.Warprints[0].Paints[%d], 0); doll.SetWarpaintColor(9, 0);",
        "tattoos": "doll.SetTattoo(doll.Tattoos[0].Paints[%d], 0); doll.SetTattooColor(9, 0);",
        "presets": "doll.SetRacePreset(doll.Race.Presets[%d]);",
    }[kind] % i
    return ev(src + ' "ok"')

VIEW = {  # kind -> (crop, zoom normalized, min zoom override, extra rotations)
    "heads": ("face", 0.0, -4.5, []), "hair": ("head", 0.0, -4.5, [150]), "beards": ("face", 0.0, -4.5, []),
    "horns": ("head", 0.0, -4.5, [90]), "scars": ("closeface", 0.0, -7.5, []), "warpaints": ("head", 0.0, -5.5, []),
    "tattoos": ("upper", 0.0, -1.5, [90, 180, 270]), "presets": ("body", 1.0, None, []),
}

def cloth(show):
    """Dress or undress the doll through the game's own visual-settings switch."""
    return ev('''
vm.ShowVisualSettings(); var vs = vm.VisualSettingsVM.Value;
if (vs != null && vs.Cloth.IsOn.Value != %s) vs.Cloth.Switch();
vm.DisposeVisualSettings(); "cloth " + doll.ShowCloth''' % ("true" if show else "false"))

def survey(kind, race=None, gender=None):
    tag = kind if race is None else "%s_%s_%s" % (kind, race, gender)
    if kind == "tattoos": print(cloth(False))
    if race:
        print(set_race_gender(race, gender))
    if kind == "presets":
        count = int(ev("doll.Race.Presets.Length").replace("=> ", ""))
        labels = ["%s_%s_preset%d" % (race, gender, i + 1) for i in range(count)]
    else:
        labels = names(kind)
    crop, z, mz, rots = VIEW[kind]
    print(zoom(z, mz))
    paths, done = [], set()
    for i, label in enumerate(labels):
        if label in done:  # the same asset reused → one capture
            continue
        done.add(label)
        set_option(kind, i)
        rotate(0)
        paths.append(capture(label, kind, crop))
        for r in rots:
            rotate(r)
            paths.append(capture(label + "_r%d" % r, kind, crop))
            rotate(0)
    if kind == "tattoos": print(cloth(True))
    labels2 = [os.path.splitext(os.path.basename(p))[0] for p in paths]
    sheets = []
    for n in range(0, len(paths), 12):
        out = os.path.join(OUT, "%s_sheet%d.png" % (tag, n // 12 + 1))
        sheets.append(sheet(paths[n:n + 12], labels2[n:n + 12], out))
    print("sheets:", *sheets, sep="\n  ")
    return sheets

if __name__ == "__main__":
    a = sys.argv[1:]
    if a and a[0] == "zoom":
        print(zoom(float(a[1]), float(a[2]) if len(a) > 2 else None))
    elif a and a[0] == "rotate":
        print(rotate(float(a[1])))
    elif a and a[0] == "paints":
        print(set_race_gender(a[1] if len(a) > 1 else "Human", a[2] if len(a) > 2 else "Female"))  # race-independent lists; Kitsune have none
        for k in ("scars", "warpaints", "tattoos"):
            survey(k)
    elif a and a[0] in VIEW:
        survey(a[0], a[1] if len(a) > 1 else None, a[2] if len(a) > 2 else None)
    else:
        print(__doc__)
