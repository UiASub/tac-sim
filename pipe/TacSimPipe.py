import socket
import json
import time

class UnityPilotClient:
    def __init__(self, host="127.0.0.1", port=5005):
        self.target = (host, port)
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    def send_command(self, move_x=0.0, move_y=0.0, move_z=0.0, turn_y=0.0, action=""):
        payload = {
            "moveX": float(move_x),
            "moveY": float(move_y),
            "moveZ": float(move_z),
            "turnY": float(turn_y),
            "action": action
        }
        data = json.dumps(payload).encode('utf-8')
        self.sock.sendto(data, self.target)

    def set_movement(self, move_x=0.0, move_y=0.0, move_z=0.0, turn_y=0.0):
        self.send_command(move_x, move_y, move_z, turn_y)

    def set_armed(self, armed=True):
        self.send_command(action="arm" if armed else "disarm")

    def close(self):
        self.sock.close()

if __name__ == "__main__":
    client = UnityPilotClient()

    print("Arming vehicle...")
    client.set_armed(True)
    time.sleep(0.5)

    print("Driving forward ('W' key) for 3 seconds...")
    start_time = time.time()
    while time.time() - start_time < 3.0:
        # move_z=1.0 is the exact equivalent of holding 'W'
        client.set_movement(move_z=1.0)
        time.sleep(0.016)  # Stream input at 60 Hz

    print("Stopping vehicle...")
    client.set_movement(0, 0, 0, 0)
    client.close()
