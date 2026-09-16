import json
import time
import sys

class UnityPipeClient:
    def __init__(self, pipe_name="TacSimPipe"):
        if sys.platform == "win32":
            self.pipe_path = rf"\\.\pipe\{pipe_name}"
        else:
            self.pipe_path = f"/tmp/{pipe_name}"
        self.pipe = None

    def connect(self):
        # Opens the pipe established by Unity
        self.pipe = open(self.pipe_path, "w")

    def send_command(self, move_x=0.0, move_y=0.0, move_z=0.0, turn_y=0.0, action=""):
        payload = {
            "moveX": float(move_x),
            "moveY": float(move_y),
            "moveZ": float(move_z),
            "turnY": float(turn_y),
            "action": action
        }
        if self.pipe:
            self.pipe.write(json.dumps(payload) + "\n")
            self.pipe.flush()

    def set_movement(self, move_x=0.0, move_y=0.0, move_z=0.0, turn_y=0.0):
        self.send_command(move_x, move_y, move_z, turn_y)

    def set_armed(self, armed=True):
        self.send_command(action="arm" if armed else "disarm")

    def close(self):
        if self.pipe:
            self.pipe.close()

if __name__ == "__main__":
    client = UnityPipeClient()
    print("Connecting to Unity Named Pipe...")
    client.connect()

    print("Arming vehicle...")
    client.set_armed(True)
    time.sleep(0.5)

    print("Driving forward for 3 seconds...")
    start_time = time.time()
    while time.time() - start_time < 3.0:
        client.set_movement(move_z=1.0)
        time.sleep(0.016)  # ~60 Hz update rate

    print("Stopping...")
    client.set_movement(0, 0, 0, 0)
    client.close()
