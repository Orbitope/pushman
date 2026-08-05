# Pushman — promo kit

**Article:** *How Not to Train a Bot to Play Your Game*
**Slot:** Week 4 — r/Unity3D Tue 25 Aug · r/gamedev Thu 27 Aug · X Wed 26 Aug

> **Schedule note:** the plan originally put r/gamedev on Screenshot Saturday. Moved to Thu 27 Aug —
> Screenshot Saturday is for visual showcases, and this is a failure retrospective. It'll do better
> as a weekday text post.

## Links

| Where | URL | Status |
|---|---|---|
| Article | `https://orbitope.github.io/pushman/` | ⚠️ **unverified** — inferred from the confirmed `orbitope.github.io/simulacrum/` pattern. Open it before posting. |
| Repo | `https://github.com/orbitope/pushman` | goes in a **comment**, never the post body |
| Playable | none — no public build | |

## The hook

A two-player ring-out fighting game: push beats dodge, dodge beats block, block beats push. The
rewards looked sane and the training curve looked healthy the entire time, but **a dodge is one
button and a push is a four-step commitment, and both paid about the same** — so the agent stopped
pushing. Block only counters push, so block died with it. **Two-thirds of the rock-paper-scissors
went extinct and the reward curve never flinched.**

Then the bots couldn't aim, because they'd been paid to face the opponent without ever being told
whether they were. The fix was `Vector2.Dot(transform.up, dirToOpp)` — one number, handed over
instead of learned. Adding it reshaped layer 1, so: full retrain from scratch.

Ending worth keeping: **good bot, not a fun game.**

---

## Reddit — r/Unity3D · Tue 25 Aug

**Title**

```text
ML-Agents gotcha: adding one observation throws away every weight you trained
```

**Body**

```text
I was paying my fighting-game agents to face their opponent, but never gave them the one number that
says whether they are — so their pushes and blocks pointed at nothing while they tried to learn
trigonometry from raw angles. Fix was one line, Vector2.Dot(transform.up, dirToOpp), handed over
precomputed. Cost was a full retrain: change the observation vector and the first layer is the wrong
shape, so nothing carries forward. Change only rewards and you can warm-start off the weights you
have, bump the LR, and climb out of the local optimum instead of starting from a blank page.
```

**Link:** the article.

**Image:** `img/aiming-dot-product.png` — the push cone and the single dot-product number.
`img/obs-shape-mismatch.png` is the alternative if you'd rather lead with the retrain gotcha itself.

**Top-level comment**

```text
Whole devlog, including the reward bug that quietly killed two of my three moves:
https://orbitope.github.io/pushman/

Project's here (Unity + ML-Agents, 45M steps, round-robin rather than self-play — self-play was the
wrong call and there's a section on why): https://github.com/orbitope/pushman
```

---

## Reddit — r/gamedev · Thu 27 Aug

**Title**

```text
My reward function quietly deleted two-thirds of my combat system and the training curve looked fine
```

**Body**

```text
Push beats dodge, dodge beats block, block beats push. A dodge is a single input; a push is charge,
hold, release, and still be standing there when it lands. They paid about the same reward, so the
agent stopped pushing — and since block only counters a push, block died too. I was left with
rock-paper-scissors that was just rock, and nothing in the metrics said so. The fix wasn't a bigger
number, it was making the win dwarf any run of hits and paying most for hits that shove someone
toward the edge.
```

**Link:** the article.

**Image:** `img/reward-values-old-vs-new.png` — the whole retune in one figure.

**Top-level comment**

```text
Full write-up, including the part where I got good bots and discovered that wasn't the same thing as
a good opponent: https://orbitope.github.io/pushman/

Source: https://github.com/orbitope/pushman
```

---

## X — Wed 26 Aug

**Tweet 1** — attach `img/combat-triangle.png`

```text
My fighting-game bots stopped pushing.

Rewards looked sane. Training curve looked healthy the whole time.

A dodge is one button. A push is charge, hold, release, and still be standing there when it lands.
Both paid about the same.

So the agent quit doing the hard one.
```
`chars: 271/280`

**Tweet 2** — attach `img/reward-values-old-vs-new.png`

```text
Push withered out of the policy. Block only counters a push, so block went with it.

Two-thirds of my rock-paper-scissors went extinct and the reward curve never flinched.

The fix wasn't a bigger number. It was making the win dwarf any run of hits.
```
`chars: 249/280`

**Tweet 3** — attach `img/aiming-dot-product.png`

```text
Next problem: they couldn't aim.

I'd been paying them to face the opponent without ever telling them whether they were — asking a
handful of neurons to derive trig from raw angles.

Fix: Vector2.Dot(transform.up, dirToOpp). Compute it yourself, hand it over.
```
`chars: 259/280`

**Tweet 4** — attach `img/warm-start-local-optimum.png`

```text
That one number cost a full retrain. Add an observation and layer 1 is the wrong shape; every weight
you trained is garbage.

Change only rewards? Warm-start, raise the LR, climb out of the local optimum.

Pick your observations early.

https://orbitope.github.io/pushman/
```
`chars: 260/280`

---

## LinkedIn — Wed 26 Aug

Short post + link, matching the format that already worked for you — not a long-form narrative.
Body stays link-free; post the link yourself as the first comment once it's up.

**Post**

```text
My fighting-game RL agents stopped pushing, and the training curve never showed it.

Push beats dodge, dodge beats block, block beats push — but a dodge is one input and a push is a
four-step commitment (charge, hold, release, land). I paid them about the same reward. So the agent
quietly abandoned the hard move, and since block only counters a push, block died with it. Two-thirds
of the combat system went extinct while the reward curve looked perfectly healthy.

The fix wasn't a bigger number — it was making the win dwarf any run of hits, so the easy move
stopped being the profitable one.

Second lesson, cheaper to avoid: the agents couldn't aim, because I was paying them to face their
opponent without ever telling them whether they were. One number fixed it — a precomputed dot product
between heading and direction-to-opponent — but adding that observation reshaped the network's first
layer, so it cost a full retrain. Reward changes let you warm-start; observation changes don't.

Full devlog in the comments.
```

**Hashtags:** `#ReinforcementLearning #GameDev #Unity3D`
**Image:** `img/combat-triangle.png` or `img/aiming-dot-product.png`

**First comment**

```text
Write-up: https://orbitope.github.io/pushman/
Code: https://github.com/orbitope/pushman
```

---

## Asset index — `docs/promo/img/`

Interactive figures captured from `docs/index.html`; the rest are the committed `docs/diagrams/*.svg`
rasterized at 3× by `scripts/capture_promo.mjs`. Nothing is redrawn.

| File | What it is |
|---|---|
| `combat-triangle.png` | push/dodge/block, each beating one and losing to another |
| `reward-values-old-vs-new.png` | the reward retune — best single image for the extinction story |
| `aiming-dot-product.png` | the push cone and the one number that decides whether it lands |
| `aiming-interactive.png` | the same thing from the article's draggable widget |
| `obs-shape-mismatch.png` | why adding an observation invalidates the network |
| `warm-start-local-optimum.png` | reward change → warm-start; observation change → start over |
| `reaction-lag.mp4` / `.gif` | the difficulty-tier reaction-lag ghost sweeping through its range |
| `state-machine.png`, `observation-table.png`, `reward-table.png` | the reference diagrams |
| `self-play-vs-round-robin.png` | why self-play was the wrong call |
| `personality-one-hot.png`, `personality-rewards.png`, `personality-one-hot-matrix.png`, `personality-reward-emphasis.png` | the five personalities |

---

## Appendix — the five-week calendar

Warm-up **Wed 5 – Thu 6 Aug**: ordinary commenting, no links, in r/WebGames and r/puzzles. Keep
low-level commenting going in each week's target subs throughout — with a new account this matters
more than any single post.

| Week | Reddit #1 | Reddit #2 | X | LinkedIn |
|---|---|---|---|---|
| 1 | Thu 6 Aug — **Gridlocked** → r/WebGames | Sat 8 Aug — r/puzzles | — (already posted) | Wed 5 Aug |
| 2 | Tue 11 Aug — **Hex Truchet** → r/proceduralgeneration | Thu 13 Aug — r/tabletopgamedesign | Wed 12 Aug | Wed 12 Aug |
| 3 | Tue 18 Aug — **Simulacrum** → r/reinforcementlearning | Thu 20 Aug — r/MachineLearning `[P]` (gated) | Wed 19 Aug | Wed 19 Aug |
| 4 | Tue 25 Aug — **Pushman** → r/Unity3D | Thu 27 Aug — r/gamedev | Wed 26 Aug | Wed 26 Aug |
| 5 | Tue 1 Sep — **RLevator** → r/reinforcementlearning | Thu 3 Sep — r/MachineLearning `[P]` (gated) | Wed 2 Sep | Wed 2 Sep |

Reddit posts land Tuesday mornings US-Eastern; the second sub is staggered two days so two threads
are never live at once. X threads go Wednesday, a day behind Reddit, so a good comment can be folded
in. **r/MachineLearning is gated on account standing** — skip it if the account is still thin; both
RL projects stand fine on r/reinforcementlearning alone. r/algorithms is deliberately unused: best
topical fit for Gridlocked, but hostile to self-promotion from a new account. Revisit after week 5.

LinkedIn rides the same Wednesday slot as X — one extra post to draft per week, no new day added.
Body stays link-free on every LinkedIn post; the link goes in your own first comment once it's up,
same convention as Reddit. Week 1's LinkedIn post (Wed 5 Aug) is the exception that runs a day ahead
of the Reddit warm-up, since LinkedIn has no comment-karma ramp to respect.

Pushman deliberately does **not** go to r/reinforcementlearning — that sub already gets Simulacrum
and RLevator, and this one plays better to a gamedev audience anyway.

Nothing here posts itself. Reddit and X both punish anything that reads as automated, and the
comment replies are most of the value.
