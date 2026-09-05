import importlib.util
import pathlib
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("rauchtest", pathlib.Path(__file__).with_name("rauchtest.py"))
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)


class Fake:
    def __init__(self, fail=False):
        self.requests = []
        self.fail = fail

    def send(self, **request):
        self.requests.append(request)
        if request["cmd"] == "schema":
            return {"ok": True, "game": "How to Fish", "ready": True,
                    "categories": [{"options": [{"id": "esp.players", "kind": "Toggle"},
                                                   {"id": "money.add", "kind": "Button"}]}],
                    "values": {"esp.players": {"bool": False}}}
        if self.fail and request.get("bool") is True:
            raise OSError("response lost")
        return {"ok": True}

    def close(self):
        pass


class SmokeTests(unittest.TestCase):
    def test_default_never_mutates(self):
        fake = Fake()
        smoke.inspect(fake)
        self.assertEqual(fake.requests, [{"cmd": "schema"}])

    def test_allowlist_excludes_buttons_and_restores(self):
        fake = Fake()
        smoke.inspect(fake, True)
        self.assertEqual(fake.requests[1:], [{"cmd": "set", "id": "esp.players", "bool": True},
                                            {"cmd": "set", "id": "esp.players", "bool": False}])

    def test_restoration_after_lost_response_and_failure_exit(self):
        fake = Fake(fail=True)
        with patch.object(smoke, "Floppy", return_value=fake):
            self.assertEqual(smoke.main(["--exercise"]), 1)
        self.assertEqual(fake.requests[-1], {"cmd": "set", "id": "esp.players", "bool": False})


if __name__ == "__main__":
    unittest.main()
