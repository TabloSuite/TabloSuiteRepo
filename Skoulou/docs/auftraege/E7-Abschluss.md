# Agenten-Auftrag E7 « Abschluss » (Stand 14.11.2026)

Zwei Teile, nacheinander (Nutzer: nur 1 Agent gleichzeitig): **E7a Simulation** → danach **E7b Rundgang + Feinschliff**.
Basis: Hauptbranch nach 1ce7b5e (E1–E6 zusammengeführt, Migration « Fundament », 540 Tests grün). Der Neubau ist seit 14.11.
auf skoulou.tablosuite.com live (Azure-DB mit Schema « Fundament », nur Testdaten).
Falls unterbrochen: neuen Agenten mit dem jeweiligen Teil starten und ergänzen: « Continue: review what already exists
(git status/diff, Skoulou/Skoulou.Tests/Simulation, docs/Simulation-Bericht.md), then finish the scope. »

---

## Gemeinsame Regeln (für beide Teile)

You work directly in the main working tree `C:\ETSE\NETAPPS\TabloSuiteData\TabloSuiteProjekt` (no other agent touches
`Skoulou/` while you run). Stack: Blazor Server .NET 10, EF Core SQL Server, French UI with « vous », German code
identifiers. Market: Togo, FCFA whole francs, cheap phones.

READ FIRST: `Skoulou/docs/Bauplan.md` (binding – especially §3 roles/rights, §4 data, §5 rules, §6 routes, §7 UI, §8
conventions, §9 tests, §10, §11 decisions incl. platform invoice PER PERIOD, language schools with « Sessions », §12).
Fachkonzepte `Skoulou/docs/fachkonzept/01…05` only where you need detail. Then the code: `Skoulou.Core`,
`Skoulou.WebApp/Services/*`, `Skoulou.Tests` (helpers in `Hilfen/`, SQL opt-in tests in `Sql/`).

- Bugs you find: fix them at the root (any area), with a regression test. Keep fixes small; do not restructure.
- **Schema:** avoid entity/model changes. If one is unavoidable, add a NEW incremental migration
  (`dotnet ef migrations add <Name> --project Skoulou/Skoulou.WebApp --configuration Release`) – never regenerate or edit
  « Fundament » (the live Azure DB has it). List every schema change in the report; applying it to Azure is the lead's job
  (only with the owner's OK).
- Databases: only your own local DBs (`SkoulouDB_SIM`, random `SKOULOU_SIM_*`/`SKOULOU_TEST_*`); never touch `SkoulouDB`
  except reading, never Azure, no `az`, no push, no real mails (outbox only). Never print secrets/passwords (demo
  credentials only into files under `%TEMP%\skoulou-neu\`).
- Only stop processes you started (check the parent process; never kill Visual Studio's devenv/VsDebugConsole children).
- Temp files to `C:\Users\etsec\AppData\Local\Temp\skoulou-neu\E7\`, moved to the Recycle Bin at the end (PowerShell
  Microsoft.VisualBasic FileSystem SendToRecycleBin). Write scripts with the Write tool; PowerShell scripts UTF-8 with BOM.
- Verify before committing: `dotnet build Skoulou/Skoulou.WebApp/Skoulou.WebApp.csproj` 0 warnings; `dotnet test
  Skoulou/Skoulou.Tests/Skoulou.Tests.csproj` green; once with `SKOULOU_SQL_TESTS=1`.
- Commit green states on the current branch (message starting « SKOULOU-Neubau E7a: … » / « E7b: … », last line
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`). Do not push (the lead pushes).
- Work economically (the owner has a tight usage limit): read files in excerpts, look only at the screenshots you need.

---

## E7a – Simulation (Bauplan §9.4)

1. New folder `Skoulou/Skoulou.Tests/Simulation/` with an opt-in test (`SKOULOU_SIM=1`, skipped otherwise) on a real SQL
   Server DB (random `SKOULOU_SIM_*`, dropped afterwards; with `SKOULOU_SIM_BEHALTEN=SkoulouDB_SIM` the DB is kept under
   that name for E7b). Deterministic random generator (fixed seed), realistic French/Togolese fictional names.
2. Drive everything through the **real services** (not raw inserts) so rights and rules are exercised: SuperAdmin + 2
   further admins, 3 Responsables; **≥ 20 schools** (Primaire, Collège, Lycée, ≥ 2 language schools with 3–4 « Sessions »
   per year and classes running only in one session); per school registration → validation (some refused/suspended),
   staff (direction, coordinators with/without finance right, teachers), levels/classes/subjects/assignments,
   **several thousand students** in total + parents, enrollments incl. class changes and departures mid-period.
3. **3 school years** each: fee items + instalments + reductions, payments (cash/Mobile Money), cancellations with reason,
   manual journal entries; lessons/exams + attempts (sample); grades, conseil, publication of bulletins, one
   rectification; platform invoices per period (generate, declare, confirm, contest, cancel, supplementary invoice for a
   retroactive enrollment), Responsable shares + payouts; year change wizard with promotion/redoublement/departure.
4. **Invariants** (assert, with clear messages): year price split sums exactly to P per student and year; each student
   billed at most once per period across class changes; no gaps/duplicates in receipt/bulletin/invoice numbers; cash
   balance = sum of journal; family balances = due − paid (+ arrears carried over); ranks consistent with averages;
   rights: every role gets « refusé » for other schools' data (sample calls); Responsable shares only for confirmed
   invoices, negative compensation after cancellation.
5. **Response times**: measure the main list queries (students, payments, journal, grade grid, invoices, admin schools)
   on the full data; anything > 500 ms → fix (index, projection, paging) and re-measure.
6. Write `Skoulou/docs/Simulation-Bericht.md` (German, short): data volume, run time, invariants checked, bugs found +
   fixed (with commit), timings before/after, open points. Commit.

FINAL REPORT E7a (≤ 30 lines): commits, volume, bugs fixed, schema changes (exact), timings, open points, whether
`SkoulouDB_SIM` was kept for E7b.

---

## E7b – Browser-Rundgang + Feinschliff (Bauplan §9.5, §10 E7)

1. Run the app locally on `SkoulouDB_SIM` (env `ConnectionStrings__DefaultConnection=Server=localhost;Database=SkoulouDB_SIM;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Encrypt=True`,
   `--urls http://localhost:5181`; if the DB is missing, run the simulation with `SKOULOU_SIM_BEHALTEN` first).
   Accounts: set known passwords for one account per role via a small helper (written only to
   `%TEMP%\skoulou-neu\E7\konten.json`). On a fresh DB every account first accepts the terms (« J'accepte et je continue »).
2. Playwright (`playwright-core` in `C:\Users\etsec\AppData\Local\Temp\skoulou-pw\node_modules`, put the script there,
   `chromium.launch({ channel: 'msedge', headless: true })`): for **every role** (SuperAdmin, Admin, Responsable,
   Direction, Coordinateur, Enseignant, Élève, Parent, Visiteur) visit every page of its navigation + main dialogs,
   desktop 1440×900 and mobile 390×844. Log console errors, failed requests/500s, load time per page, horizontal
   overflow on mobile, texts without accents / placeholder texts / English words, empty states without explanation.
   Mask e-mails. Pictures to `C:\Users\etsec\Pictures\SKOULOU-Neubau-E7-Rundgang\<rolle>\`.
3. Fix what you find (with tests where logic is involved); repeat the tour until clean. Pages slower than 1 s on the big
   data → fix.
4. Guide/Handbuch: review the role guides (built in E2) against the real pages; texts complete, correct, simple French
   with « vous ».
5. Update `Skoulou/docs/Simulation-Bericht.md` (section « Rundgang »: pages, errors found/fixed, slow pages). Commit.

FINAL REPORT E7b (≤ 30 lines): commits, pages visited per role, errors found/fixed, schema changes (exact), remaining
open points, picture folder (name the 6–8 most telling pictures).
