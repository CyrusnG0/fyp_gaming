"""Declared scenario geometry: a documented static map, never the run's hidden state.

The classification answers one narrow question — did a march hex reduce or increase the map-graph
distance to the scenario's declared centre? It is geometry, not intent, and it never claims that a
seat could see the objective.
"""
import json

from experiment_context_constants import SCENARIO_GEOMETRY
from experiment_context_paths import display_path


class Geometry:
    """Declared scenario geometry. Never reads later state or hidden entities."""

    def __init__(self, scenario_id, map_data=None, map_status="missing", map_source=None):
        self.scenario_id = scenario_id
        self.spec = SCENARIO_GEOMETRY.get(scenario_id)
        self.map = map_data
        self.map_status = map_status
        self.map_source = map_source
        self._cache = {}

    @property
    def centre(self):
        return None if self.spec is None else self.spec["centre"]

    def status(self):
        if self.spec is None:
            return "unknown:unsupported_scenario"
        if self.map is None:
            return "unknown:" + self.map_status
        return "ok"

    def distance(self, point):
        """Map-graph steps to the scenario centre, or None when geometry cannot answer."""
        if self.spec is None or self.map is None or point is None:
            return None
        point = (int(point[0]), int(point[1]))
        if point in self._cache:
            return self._cache[point]
        walkable = self.map["walkable"]
        adjacency = self.map["adjacency"]
        centre = self.centre
        if point == centre:
            self._cache[point] = 0
            return 0
        if point not in walkable:
            self._cache[point] = None
            return None
        queue = [(centre, 0)]
        seen = {centre}
        found = None
        head = 0
        while head < len(queue):
            node, distance = queue[head]
            head += 1
            for neighbour in adjacency.get(node, ()):
                if neighbour in seen or not walkable.get(neighbour):
                    continue
                if neighbour == point:
                    found = distance + 1
                    break
                seen.add(neighbour)
                queue.append((neighbour, distance + 1))
            if found is not None:
                break
        self._cache[point] = found
        return found

    def relation(self, origin, destination):
        """Geometric toward/away/same relative to the scenario centre. Not an intent label."""
        info = {"status": "unknown", "reason": None, "metric": "map_graph_steps",
                "centre": list(self.centre) if self.centre else None,
                "centre_label": self.spec["centre_label"] if self.spec else None,
                "origin_distance": None, "destination_distance": None, "relation": "unknown"}
        if self.spec is None:
            info["reason"] = "unsupported_scenario"
        elif self.map is None:
            info["reason"] = self.map_status
        elif origin is None or destination is None:
            info["reason"] = "missing_position"
        else:
            before = self.distance(origin)
            after = self.distance(destination)
            info["origin_distance"] = before
            info["destination_distance"] = after
            if before is None or after is None:
                info["reason"] = "position_not_in_geometry"
            else:
                info["status"] = "ok"
                info["relation"] = ("toward" if after < before else
                                    "away" if after > before else "same")
        if info["status"] != "ok":
            info["status"] = "unknown:" + str(info["reason"])
        return info

    def toward_centre_options(self, origin, options):
        """Displayed options that reduce the map-graph distance to the centre."""
        if self.spec is None or self.map is None or origin is None or not options:
            return None
        before = self.distance(origin)
        if before is None:
            return None
        result = []
        for option in options:
            after = self.distance((option["x"], option["z"]))
            if after is not None and after < before:
                result.append({"x": option["x"], "z": option["z"], "cost": option["cost"],
                               "distance": after})
        return result

    def to_json(self):
        return {"scenario_id": self.scenario_id, "status": self.status(),
                "metric": "map_graph_steps (BFS over demo-map.json adjacency)",
                "centre": list(self.centre) if self.centre else None,
                "centre_label": self.spec["centre_label"] if self.spec else None,
                "map_source": self.map_source}


def load_map(path):
    """Load the exported demo map (walkability + adjacency) if it is present."""
    try:
        with open(path, "r", encoding="utf-8-sig") as stream:
            data = json.load(stream)
    except (OSError, ValueError):
        return None, "map_unreadable"
    walkable = {}
    adjacency = {}
    try:
        for node in data.get("nodes") or []:
            walkable[(int(node["x"]), int(node["z"]))] = bool(node.get("walkable"))
        for name, neighbours in (data.get("neighbors") or {}).items():
            x_text, z_text = name.split(",")
            adjacency[(int(x_text), int(z_text))] = [
                (int(item[0]), int(item[1])) for item in neighbours]
    except (KeyError, TypeError, ValueError):
        return None, "map_malformed"
    if not walkable:
        return None, "map_empty"
    return {"walkable": walkable, "adjacency": adjacency}, "ok"


def geometry_for(experiment, map_path=None, repo_root=None, auto_map=True):
    """Build the geometry from the run's own scenario id (never from a guess)."""
    scenario_id = None
    for request in experiment.joined.values():
        if request.run is not None:
            scenario_id = request.run.get("scenario_id")
            if scenario_id:
                break
    if scenario_id is None:
        for request in experiment.joined.values():
            observation, status, _ = request.observation()
            if status == "ok" and observation:
                scenario_id = observation.get("scenario_id")
                if scenario_id:
                    break
    spec = SCENARIO_GEOMETRY.get(scenario_id)
    if map_path is None and auto_map and spec is not None and repo_root is not None:
        candidate = repo_root / spec["map_relative_path"]
        map_path = candidate if candidate.exists() else None
    map_data, map_status = (None, "map_missing")
    if map_path is not None:
        map_data, map_status = load_map(map_path)
    return Geometry(scenario_id, map_data, map_status,
                    display_path(map_path, repo_root) if map_path is not None else None)
