"""Negative controls for report admission; these do not prove native Git works."""
import copy
import unittest
from run import PACKAGE, cleanup_owned, validate_report, verify_stopped


class ReportAdmissionTests(unittest.TestCase):
    def setUp(self):
        self.report = dict(schema=1, package=PACKAGE, pid=123, abi="x86_64", runId="a" * 32,
                           negative=False, success=True, handlesBefore="crypto=0xabc;ssl=0xdef",
                           handlesAfter="crypto=0xabc;ssl=0xdef", operations=[])
        for name in ("first", "second"):
            self.report["operations"].append(dict(repository=f"/data/user/0/{PACKAGE}/files/native-git-smoke/{name}",
                commit=("a" if name == "first" else "b") * 40, init=True, staged=True,
                committed=True, clean=True, reopenedExactBlob=True))

    def test_actual_complete_positive_shape_admitted(self):
        validate_report(self.report, "123", False)

    def test_stale_pid_rejected(self):
        with self.assertRaises(AssertionError):
            validate_report(self.report, "124", False)

    def test_each_incomplete_or_wrong_success_contract_rejected(self):
        changes = ({"package": "com.Kibnet.Unlimotion"}, {"abi": "arm64-v8a"}, {"success": False},
                   {"operations": []}, {"handlesAfter": "crypto=0x0;ssl=0x0"}, {"runId": ""},
                   {"negative": True}, {"error": "hidden failure"})
        for change in changes:
            with self.subTest(change=change), self.assertRaises(AssertionError):
                validate_report(self.report | change, "123", False)

    def test_unverified_blob_or_external_repository_rejected(self):
        for key, value in (("reopenedExactBlob", False), ("repository", "/storage/emulated/0/Tasks")):
            report = copy.deepcopy(self.report)
            report["operations"][0][key] = value
            with self.subTest(key=key), self.assertRaises(AssertionError):
                validate_report(report, "123", False)

    def test_negative_requires_actual_native_failure_and_zero_operations(self):
        report = self.report | dict(negative=True, success=False, operations=[], error="TypeInitializationException: NativeMethods")
        validate_report(report, "123", True)
        for change in ({"success": True}, {"error": "unrelated exception"}, {"operations": self.report["operations"]}):
            with self.subTest(change=change), self.assertRaises(AssertionError):
                validate_report(report | change, "123", True)


class CleanupAdmissionTests(unittest.TestCase):
    def test_transport_empty_stdout_cannot_prove_exit(self):
        for response in (b"", b"OWN_APP_RUNNING\n"):
            with self.subTest(response=response), self.assertRaises(AssertionError):
                verify_stopped(lambda *args: response)
        verify_stopped(lambda *args: b"OWN_APP_STOPPED\n")

    def test_each_cleanup_failure_still_attempts_all_steps(self):
        for fail_index in range(3):
            calls = []

            def fake_adb(*args):
                calls.append(args)
                if len(calls) - 1 == fail_index:
                    raise RuntimeError("Injected transport failure")
                return b"OWN_APP_STOPPED\n"

            errors = cleanup_owned(fake_adb)
            self.assertEqual(len(calls), 3)
            self.assertEqual(len(errors), 1)
            self.assertIn("Injected transport failure", errors[0]["error"])


if __name__ == "__main__":
    unittest.main()
