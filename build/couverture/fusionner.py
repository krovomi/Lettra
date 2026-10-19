"""Fusionne les rapports Cobertura (un par projet de tests) et imprime la couverture par assembly."""
# Usage : python3 build/couverture/fusionner.py <dossier des rapports> [nombre de fichiers à lister]
#                [--seuil-lignes 88] [--seuil-branches 78]   (code de sortie 1 sous le seuil, pour la CI)
# Une ligne est couverte si au moins un projet de tests l'exécute ; le code généré est écarté.
import glob, re, sys, xml.etree.ElementTree as ET
from collections import defaultdict

args = sys.argv[1:]
seuils = {}
for nom in ("--seuil-lignes", "--seuil-branches"):
    if nom in args:
        i = args.index(nom)
        seuils[nom] = float(args[i + 1])
        del args[i:i + 2]
root_dir = args[0]
excl = re.compile(r"(Migrations/|\.Designer\.cs$|ModelSnapshot|/obj/|\.g\.cs$|\.razor\.cs$|AssemblyInfo)")
lines = defaultdict(lambda: defaultdict(int))        # (asm, file) -> line -> passages
branches = defaultdict(dict)                          # (asm, file) -> line -> (couvertes, total)
for f in glob.glob(f"{root_dir}/**/*.cobertura.xml", recursive=True):
    t = ET.parse(f).getroot()
    for pkg in t.iter("package"):
        asm = pkg.get("name")
        if asm.endswith(".Tests") or asm.startswith(("Anthropic", "xunit", "bunit", "Microsoft.", "System.", "FluentAssertions", "Testcontainers", "Docker")):
            continue
        for cls in pkg.iter("class"):
            fn = cls.get("filename")
            if excl.search(fn.replace("\\", "/")):
                continue
            for ln in cls.iter("line"):
                n = int(ln.get("number")); h = int(ln.get("hits"))
                d = lines[(asm, fn)]
                d[n] = max(d[n], h)
                cc = ln.get("condition-coverage")
                if cc:
                    m = re.search(r"\((\d+)/(\d+)\)", cc)
                    c, tot = int(m.group(1)), int(m.group(2))
                    prev = branches[(asm, fn)].get(n, (0, tot))
                    branches[(asm, fn)][n] = (max(prev[0], c), tot)

per = defaultdict(lambda: [0, 0, 0, 0])
files = []
for (asm, fn), d in lines.items():
    cov = sum(1 for h in d.values() if h > 0); tot = len(d)
    b = branches.get((asm, fn), {})
    bc = sum(c for c, _ in b.values()); bt = sum(t for _, t in b.values())
    p = per[asm]; p[0] += cov; p[1] += tot; p[2] += bc; p[3] += bt
    files.append((tot - cov, asm, fn, cov, tot))

T = [0, 0, 0, 0]
print(f"{'Assembly':55} {'lignes':>14} {'%':>6} {'branches':>14} {'%':>6}")
for asm in sorted(per, key=lambda a: per[a][0] / max(per[a][1], 1)):
    c, t, bc, bt = per[asm]
    for i, v in enumerate(per[asm]): T[i] += v
    print(f"{asm:55} {c:>6}/{t:<7} {100*c/max(t,1):5.1f} {bc:>6}/{bt:<7} {100*bc/max(bt,1):5.1f}")
print(f"{'TOTAL':55} {T[0]:>6}/{T[1]:<7} {100*T[0]/T[1]:5.1f} {T[2]:>6}/{T[3]:<7} {100*T[2]/max(T[3],1):5.1f}")
if len(args) > 1:
    print("\nFichiers avec le plus de lignes non couvertes :")
    for miss, asm, fn, cov, tot in sorted(files, reverse=True)[: int(args[1])]:
        print(f"{miss:5} non couvertes  {cov}/{tot}  {fn.split('/src/')[-1]}")

mesures = {"--seuil-lignes": 100 * T[0] / max(T[1], 1), "--seuil-branches": 100 * T[2] / max(T[3], 1)}
sous = [f"{nom[8:]} {mesures[nom]:.1f} % < {seuil:g} %" for nom, seuil in seuils.items() if mesures[nom] < seuil]
if sous:
    print("\nCouverture sous le seuil : " + ", ".join(sous))
    sys.exit(1)
