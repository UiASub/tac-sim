# TAC Sim automation demo

Build the Unity player, then launch it from the repository root with the local
automation server enabled:

```bash
./run.sh -automationPort 8765
```

In another terminal:

```bash
cd automation-demo
uv sync
uv run demo.py
```

The OpenCV window displays the forward-camera observation and telemetry. Keys
latch an axis until it is changed or neutralized with `0`; press `M` to toggle a
small yellow-target vision controller. This controller is intentionally simple:
the useful integration example is the `uint8` BGR observation and the request /
telemetry loop in `tacsim_client.py`, which can be replaced by a policy model.

The simulator listens only on `127.0.0.1`. It accepts one client at a time and
neutralizes command input after 500 ms without a `command` request. Closing the
client returns control to the normal keyboard/gamepad pilot.
