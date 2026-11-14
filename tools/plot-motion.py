import argparse
import csv
import json
from pathlib import Path

import matplotlib
import numpy as np
matplotlib.use("Agg")
import matplotlib.pyplot as plt

FPS = 60
CORNER_KEYS = [f"p{i}{axis}" for i in range(4) for axis in "xy"]


def read_trace(path):
    with path.open(encoding="utf-8") as stream:
        rows = list(csv.DictReader(stream))
    words = {}
    for row in rows:
        words.setdefault(row["text"], []).append(row)
    first = next(iter(words.values()))
    time = np.array([float(row["seconds"]) for row in first])
    camera = np.array([[float(row[f"cam_{axis}"]) for axis in "xyz"] for row in first])
    projected = {}
    for text, values in words.items():
        points = np.array([[float(row[key]) for key in CORNER_KEYS] for row in values]).reshape(-1, 4, 2)
        mask = np.array([row["visible"] == "1" for row in values])
        projected[text] = points, mask
    return time, camera, projected


def projected_speed(projected, half_window):
    tracks = {}
    span = slice(half_window, -half_window)
    for text, (points, visible) in projected.items():
        valid = visible[span] & visible[2 * half_window:] & visible[:-2 * half_window]
        delta = points[2 * half_window:] - points[:-2 * half_window]
        speed = np.sqrt((delta ** 2).sum(axis=2).mean(axis=1)) * FPS / (2 * half_window)
        tracks[text] = np.pad(np.where(valid, speed, np.nan), half_window, constant_values=np.nan)
    return tracks


def quiet_spans(time, projected, limit=50):
    tracks = projected_speed(projected, 30)
    active = np.array(list(tracks.values()))
    speed = np.where(np.isfinite(active), active, -np.inf).max(axis=0)
    quiet = (speed >= 0) & (speed < limit)
    edges = np.flatnonzero(np.diff(np.r_[False, quiet, False]))
    return [(round(float(time[a]), 3), round(float(time[b - 1]), 3)) for a, b in zip(edges[::2], edges[1::2]) if b - a >= 18]


def plot_demo(before, after, output):
    fig, axes = plt.subplots(3, 1, figsize=(12, 8), sharex=True, layout="constrained")
    for trace, label, line in [(before, "Studio inicial", "--"), (after, "Rig revisado", "-")]:
        t, camera, projected = trace
        axes[0].plot(t, np.linalg.norm(np.gradient(camera, 1 / FPS, axis=0), axis=1), line, label=label)
        velocities = projected_speed(projected, 30)
        stacked = np.array(list(velocities.values()))
        lead = np.where(np.isfinite(stacked), stacked, -np.inf).max(axis=0)
        lead[lead < 0] = np.nan
        axes[1].plot(t, lead, line, label=label)
        print(f"{label}: intervalos < 50 px/s por ao menos 0,30 s = {quiet_spans(t, projected)}")
    colors = ["#d06528", "#1975a6", "#6959ae"]
    for (word, speed), color in zip(projected_speed(after[2], 1).items(), colors):
        axes[2].plot(after[0], speed, color=color, label=word)
    axes[0].set_ylabel("Travessia da câmera\n(unidades/s)")
    axes[1].set_ylabel("Movimento coerente\nna tela (px/s)")
    axes[1].axhline(50, color="#a1a1a8", lw=1, label="50 px/s, referência diagnóstica")
    axes[2].set_ylabel("Velocidade projetada\npor palavra (px/s)")
    axes[2].set_xlabel("Tempo da sequência (s)")
    for ax in axes:
        ax.grid(alpha=.27)
        ax.legend(loc="upper left", ncol=3, fontsize=8)
    axes[1].set_ylim(bottom=0)
    axes[2].set_ylim(bottom=0)
    fig.suptitle("Continuidade do movimento · LyricSpace · projeção a 1920 × 1080", fontsize=15)
    fig.savefig(output / "LyricSpace-velocidades.png", dpi=140)
    plt.close(fig)


def plot_reference(trace, audit_directory, native_world, output):
    raw = json.loads((audit_directory / "ae_animation_samples.json").read_text(encoding="utf-8-sig"))
    audit = next(c for c in raw["compositions"] if c["id"] == 704)
    native = json.loads(native_world.read_text(encoding="utf-8-sig"))
    first = native["firstFrame"]
    last = native["lastFrame"]
    frames = np.arange(first, last + 1)
    t, actual, projected = trace
    actual = actual[:len(frames)]
    expected = np.array(native["layers"][-1]["world"][3])
    fig, axes = plt.subplots(4, 1, figsize=(12, 10), sharex=True, layout="constrained")
    for layer_id, path, label in [
        (34, "Position", "34 · impulso"), (33, "Position", "33 · recuperação"),
        (32, "Position", "32 · ponte"), (31, "Z Position", "31 · giro / deslocamento"),
        (35, "Z Rotation", "35 · roll"), (30, "Z Position", "30 · travessia"),
    ]:
        layer = next(l for l in audit["layers"] if l["index"] == layer_id)
        values = np.array(next(p for p in layer["properties"] if p["path"] == f"/Transform/{path}")["values"])
        values = values[frames - audit["firstFrame"]]
        if values.ndim == 2:
            values = values[:, 2]
        velocity = np.gradient(values, 1 / FPS)
        axes[0].plot(frames, velocity / np.max(np.abs(velocity)), label=label)
    for position, marker, label in [(expected, "-", "After Effects"), (actual, "--", "Lenoz Motion Engine")]:
        velocity = np.gradient(position, 1 / FPS, axis=0)
        speed = np.linalg.norm(velocity, axis=1)
        acceleration = np.linalg.norm(np.diff(position, n=2, axis=0) * FPS ** 2, axis=1)
        axes[1].plot(frames, speed, marker, label=label)
        axes[2].plot(frames[1:-1], acceleration, marker, label=label)
    for word, speed in projected_speed(projected, 1).items():
        axes[3].plot(first + t * FPS, speed, label=word)
    axes[0].set_ylabel("Velocidades locais\n(fração do pico)")
    axes[1].set_ylabel("Velocidade mundial\n(unidades/s)")
    axes[2].set_ylabel("Aceleração mundial\n(unidades/s²)")
    axes[3].set_ylabel("Planos de teste\nprojeção (px/s)")
    axes[3].set_xlabel("Frame exibido no After Effects (60 fps)")
    for ax in axes:
        ax.grid(alpha=.27)
        ax.legend(loc="upper left", fontsize=8, ncol=3)
    fig.suptitle("Rig original da câmera · F212 a F429", fontsize=15)
    fig.savefig(output / "Lenoz-rig-reference.png", dpi=140)
    plt.close(fig)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--before", type=Path, required=True)
    parser.add_argument("--after", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--reference", type=Path)
    parser.add_argument("--audit", type=Path)
    parser.add_argument("--native", type=Path)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    plot_demo(read_trace(args.before), read_trace(args.after), args.out)
    if args.reference and args.audit and args.native:
        plot_reference(read_trace(args.reference), args.audit, args.native, args.out)


if __name__ == "__main__":
    main()
