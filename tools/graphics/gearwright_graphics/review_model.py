"""Desktop review application with a persistent OpenGL model viewport."""

from __future__ import annotations

import argparse
import json
import math
import os
import sys
from pathlib import Path
from typing import Any

from PySide6.QtCore import QElapsedTimer, Qt, QTimer
from PySide6.QtGui import QGuiApplication, QSurfaceFormat
from PySide6.QtWidgets import (
    QApplication,
    QComboBox,
    QFormLayout,
    QGroupBox,
    QHBoxLayout,
    QLabel,
    QMainWindow,
    QPushButton,
    QScrollArea,
    QSlider,
    QSplitter,
    QVBoxLayout,
    QWidget,
)

from .live_view import LiveModelViewport, camera_position
from .reviewer import ReviewCatalog


class ReviewModelWindow(QMainWindow):
    def __init__(self, project_root: Path, review: Path | None, vintage_story: Path | None) -> None:
        super().__init__()
        self.project_root = project_root.resolve()
        self.review_request = review or Path("generated/slingshot-review")
        self.all_models = review is None
        self.catalog = ReviewCatalog(self.project_root, self.review_request, all_models=self.all_models)
        self.catalog_signature = self.catalog.model_folder_signature()
        self.vintage_story = vintage_story.resolve() if vintage_story else None
        self.play_timer = QTimer(self)
        # Vintage Story animation frames advance at 30 per second here. Match
        # the redraw cadence to that source rate so complex models do not queue
        # redundant intermediate GPU uploads.
        self.play_timer.setTimerType(Qt.TimerType.PreciseTimer)
        self.play_timer.setInterval(33)
        self.play_timer.timeout.connect(self._play_tick)
        self.play_clock = QElapsedTimer()
        self.play_start_frame = 0.0
        self.refresh_timer = QTimer(self)
        self.refresh_timer.setInterval(750)
        self.refresh_timer.timeout.connect(self._refresh_catalog_if_changed)

        first = self.catalog.candidates[0]
        first_stage = next((item for item in first["stages"] if item["id"] == "rest"), first["stages"][0])
        self.candidate_id = first["id"]
        self.stage_id = first_stage["id"]
        self.animation_code = ""
        self.frame_value = 0.0

        self.setWindowTitle(f"Gearwright model review - {self.catalog.review_root.name}")
        self.resize(1320, 820)
        self._build_ui()
        defaults = self.catalog.defaults
        self.viewport.set_camera(
            defaults["azimuth"], defaults["elevation"], defaults["distance"],
            list(defaults["target"]), defaults["scale"],
        )
        self._populate_candidates()
        self._load_current_shape()
        self.refresh_timer.start()

    def _build_ui(self) -> None:
        splitter = QSplitter(Qt.Orientation.Horizontal)
        splitter.setChildrenCollapsible(False)
        self.setCentralWidget(splitter)

        sidebar = QWidget()
        sidebar.setMinimumWidth(290)
        sidebar.setMaximumWidth(360)
        sidebar_layout = QVBoxLayout(sidebar)
        title = QLabel(self.catalog.review_root.name.replace("-", " ").title())
        title.setStyleSheet("font-size: 16px; font-weight: bold;")
        sidebar_layout.addWidget(title)
        path_label = QLabel(self.catalog.review_root.relative_to(self.project_root).as_posix())
        path_label.setWordWrap(True)
        path_label.setStyleSheet("color: #777;")
        sidebar_layout.addWidget(path_label)

        model_group = QGroupBox("Model")
        model_form = QFormLayout(model_group)
        self.candidate_box = QComboBox()
        self.candidate_box.currentIndexChanged.connect(self._candidate_changed)
        model_form.addRow("Candidate", self.candidate_box)
        self.description = QLabel()
        self.description.setWordWrap(True)
        self.description.setStyleSheet("color: #777;")
        model_form.addRow("", self.description)
        self.stage_box = QComboBox()
        self.stage_box.currentIndexChanged.connect(self._stage_changed)
        model_form.addRow("State", self.stage_box)
        self.animation_box = QComboBox()
        self.animation_box.currentIndexChanged.connect(self._animation_changed)
        model_form.addRow("Animation", self.animation_box)
        self.frame_label = QLabel("Frame -")
        self.frame_slider = QSlider(Qt.Orientation.Horizontal)
        self.frame_slider.valueChanged.connect(self._frame_changed)
        model_form.addRow(self.frame_label, self.frame_slider)
        self.play_button = QPushButton("Play animation")
        self.play_button.clicked.connect(self._toggle_play)
        model_form.addRow("", self.play_button)
        sidebar_layout.addWidget(model_group)

        camera_group = QGroupBox("Camera")
        camera_layout = QVBoxLayout(camera_group)
        self.orbit_label = QLabel()
        camera_layout.addWidget(self.orbit_label)
        horizontal_views = QHBoxLayout()
        for label, azimuth in (("Front", 180), ("Right", -90), ("Back", 0), ("Left", 90)):
            button = QPushButton(label)
            button.clicked.connect(lambda checked=False, a=azimuth: self.viewport.set_view(a, 0))
            horizontal_views.addWidget(button)
        camera_layout.addLayout(horizontal_views)
        utility_views = QHBoxLayout()
        top_button = QPushButton("Top")
        top_button.clicked.connect(lambda: self.viewport.set_view(0, 89))
        utility_views.addWidget(top_button)
        fit_button = QPushButton("Fit model")
        fit_button.clicked.connect(lambda: self.viewport.fit_model())
        utility_views.addWidget(fit_button)
        reset_button = QPushButton("Review view")
        reset_button.clicked.connect(self._reset_camera)
        utility_views.addWidget(reset_button)
        camera_layout.addLayout(utility_views)
        camera_hint = QLabel("Left drag: orbit\nMiddle/right drag: pan\nWheel: zoom")
        camera_hint.setStyleSheet("color: #777;")
        camera_layout.addWidget(camera_hint)
        sidebar_layout.addWidget(camera_group)

        copy_button = QPushButton("Copy review reference")
        copy_button.clicked.connect(self._copy_review_reference)
        sidebar_layout.addWidget(copy_button)
        reference_hint = QLabel("Paste the copied JSON into an approval comment so the exact candidate, state, frame, and camera are recorded.")
        reference_hint.setWordWrap(True)
        reference_hint.setStyleSheet("color: #777;")
        sidebar_layout.addWidget(reference_hint)
        sidebar_layout.addStretch(1)

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setWidget(sidebar)
        splitter.addWidget(scroll)

        viewport_container = QWidget()
        viewport_layout = QVBoxLayout(viewport_container)
        self.viewport = LiveModelViewport(self.project_root, self.vintage_story)
        self.viewport.cameraChanged.connect(self._camera_changed)
        self.viewport.sceneStatus.connect(self._scene_status)
        viewport_layout.addWidget(self.viewport, 1)
        self.status_label = QLabel("Preparing live viewport...")
        self.status_label.setStyleSheet("color: #777;")
        viewport_layout.addWidget(self.status_label)
        splitter.addWidget(viewport_container)
        splitter.setSizes([320, 1000])

    def _candidate_data(self) -> dict[str, Any]:
        return next(item for item in self.catalog.candidates if item["id"] == self.candidate_id)

    def _stage_data(self) -> dict[str, Any]:
        return next(item for item in self._candidate_data()["stages"] if item["id"] == self.stage_id)

    def _populate_candidates(self) -> None:
        candidate_ids = {item["id"] for item in self.catalog.candidates}
        if self.candidate_id not in candidate_ids:
            self.candidate_id = self.catalog.candidates[0]["id"]
            self.stage_id = ""
            self.animation_code = ""
        self.candidate_box.blockSignals(True)
        self.candidate_box.clear()
        for item in self.catalog.candidates:
            self.candidate_box.addItem(item["label"], item["id"])
        self.candidate_box.setCurrentIndex(max(0, self.candidate_box.findData(self.candidate_id)))
        self.candidate_box.blockSignals(False)
        self._populate_stages()

    def _populate_stages(self) -> None:
        stages = self._candidate_data()["stages"]
        valid = {item["id"] for item in stages}
        if self.stage_id not in valid:
            self.stage_id = next((item["id"] for item in stages if item["id"] == "rest"), stages[0]["id"])
        self.stage_box.blockSignals(True)
        self.stage_box.clear()
        for item in stages:
            self.stage_box.addItem(item["label"], item["id"])
        self.stage_box.setCurrentIndex(max(0, self.stage_box.findData(self.stage_id)))
        self.stage_box.blockSignals(False)
        self.description.setText(self._candidate_data()["description"])
        self._populate_animations()

    def _populate_animations(self) -> None:
        animations = self._stage_data().get("animations", [])
        if self.animation_code not in {item["code"] for item in animations}:
            self.animation_code = ""
        self.animation_box.blockSignals(True)
        self.animation_box.clear()
        self.animation_box.addItem("None" if animations else "No embedded animation", "")
        for item in animations:
            self.animation_box.addItem(item["label"], item["code"])
        self.animation_box.setCurrentIndex(max(0, self.animation_box.findData(self.animation_code)))
        self.animation_box.blockSignals(False)
        selected = self._selected_animation()
        maximum = max(0, selected["frames"] - 1) if selected else 0
        self.frame_slider.blockSignals(True)
        self.frame_slider.setRange(0, int(maximum * 10))
        self.frame_slider.setValue(min(int(self.frame_value * 10), int(maximum * 10)))
        self.frame_slider.blockSignals(False)
        self.frame_slider.setEnabled(selected is not None)
        self.play_button.setEnabled(selected is not None)
        self._update_frame_label()

    def _selected_animation(self) -> dict[str, Any] | None:
        return next((item for item in self._stage_data().get("animations", []) if item["code"] == self.animation_code), None)

    def _load_current_shape(self) -> None:
        self.play_timer.stop()
        self.play_button.setText("Play animation")
        self.viewport.load_shape(
            self.catalog.shape_path(self.candidate_id, self.stage_id),
            self.animation_code,
            self.frame_value,
        )

    def _candidate_changed(self, index: int) -> None:
        if index < 0:
            return
        self.candidate_id = str(self.candidate_box.itemData(index))
        # Keep a shared state (for example wall-mounted) for direct comparison.
        # _populate_stages selects the default if the new candidate lacks it.
        self.animation_code = ""
        self.frame_value = 0.0
        self._populate_stages()
        self._load_current_shape()

    def _stage_changed(self, index: int) -> None:
        if index < 0:
            return
        self.stage_id = str(self.stage_box.itemData(index))
        self.animation_code = ""
        self.frame_value = 0.0
        self._populate_animations()
        self._load_current_shape()

    def _animation_changed(self, index: int) -> None:
        if index < 0:
            return
        self.animation_code = str(self.animation_box.itemData(index) or "")
        self.frame_value = 0.0
        self._populate_animations()
        self.viewport.set_pose(self.animation_code, self.frame_value)

    def _frame_changed(self, value: int) -> None:
        self.frame_value = value / 10.0
        self._update_frame_label()
        self.viewport.set_pose(self.animation_code, self.frame_value)

    def _update_frame_label(self) -> None:
        self.frame_label.setText(f"Frame {self.frame_value:.1f}" if self._selected_animation() else "Frame -")

    def _toggle_play(self) -> None:
        if self.play_timer.isActive():
            self.play_timer.stop()
            self.play_button.setText("Play animation")
            return
        if not self._selected_animation():
            return
        self.play_start_frame = self.frame_value
        self.play_clock.start()
        self.play_timer.start()
        self.play_button.setText("Pause animation")

    def _play_tick(self) -> None:
        selected = self._selected_animation()
        if not selected:
            self.play_timer.stop()
            self.play_button.setText("Play animation")
            return
        self.frame_value = (self.play_start_frame + self.play_clock.elapsed() * .03) % selected["frames"]
        self.frame_slider.blockSignals(True)
        self.frame_slider.setValue(int(self.frame_value * 10))
        self.frame_slider.blockSignals(False)
        self._update_frame_label()
        self.viewport.set_pose(self.animation_code, self.frame_value)

    def _camera_changed(self, azimuth: float, elevation: float, scale: float) -> None:
        self.orbit_label.setText(f"Orbit {azimuth:.0f} deg / {elevation:.0f} deg - scale {scale:.2f}")

    def _reset_camera(self) -> None:
        defaults = self.catalog.defaults
        self.viewport.set_camera(
            defaults["azimuth"], defaults["elevation"], defaults["distance"],
            list(defaults["target"]), defaults["scale"],
        )

    def _scene_status(self, message: str) -> None:
        self.status_label.setText(message)

    def _copy_review_reference(self) -> None:
        position = camera_position(
            self.viewport.azimuth, self.viewport.elevation,
            self.viewport.distance, self.viewport.target,
        )
        reference = {
            "version": 1,
            "review": self.catalog.review_root.relative_to(self.project_root).as_posix(),
            "candidate": self.candidate_id,
            "state": self.stage_id,
            "animation": self.animation_code or None,
            "frame": round(self.frame_value, 4) if self.animation_code else None,
            "camera": {
                "position": [round(float(value), 6) for value in position],
                "target": [round(float(value), 6) for value in self.viewport.target],
                "orthographicScale": round(self.viewport.scale, 6),
            },
        }
        QGuiApplication.clipboard().setText(json.dumps(reference, indent=2))
        self.status_label.setText("Copied exact review reference to the clipboard")

    def _refresh_catalog_if_changed(self) -> None:
        signature = self.catalog.model_folder_signature()
        if signature == self.catalog_signature:
            return
        try:
            catalog = ReviewCatalog(self.project_root, self.review_request, all_models=self.all_models)
        except (OSError, ValueError) as exc:
            self.status_label.setText(f"Waiting for complete model files: {exc}")
            return
        self.catalog = catalog
        self.catalog_signature = signature
        self._populate_candidates()
        self._load_current_shape()


def discover_vintage_story() -> Path | None:
    candidates: list[Path] = []
    for variable in ("VINTAGE_STORY", "VINTAGE_STORY_PATH"):
        value = os.environ.get(variable)
        if value:
            candidates.append(Path(value))
    for variable in ("APPDATA", "LOCALAPPDATA"):
        value = os.environ.get(variable)
        if value:
            base = Path(value)
            candidates.extend((base / "Vintagestory", base / "VintageStory"))
    return next(
        (candidate for candidate in candidates if (candidate / "VintagestoryAPI.dll").is_file() and (candidate / "assets").is_dir()),
        None,
    )


def discover_project_root() -> Path:
    module_root = Path(__file__).resolve().parents[3]
    if (module_root / "graphics").is_dir() and (module_root / "tools").is_dir():
        return module_root
    return Path.cwd()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Open the Gearwright live model review app.")
    parser.add_argument("--root", type=Path)
    parser.add_argument("--review", type=Path, help="Review folder; default shows all models below generated/slingshot-review")
    parser.add_argument("--vintage-story", type=Path)
    args = parser.parse_args(argv)

    surface = QSurfaceFormat()
    surface.setVersion(3, 3)
    surface.setProfile(QSurfaceFormat.OpenGLContextProfile.CoreProfile)
    surface.setDepthBufferSize(24)
    surface.setSamples(4)
    QSurfaceFormat.setDefaultFormat(surface)
    application = QApplication(sys.argv[:1] + (argv or []))
    try:
        window = ReviewModelWindow(
            args.root or discover_project_root(),
            args.review,
            args.vintage_story or discover_vintage_story(),
        )
    except (OSError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    window.show()
    return application.exec()


if __name__ == "__main__":
    raise SystemExit(main())
