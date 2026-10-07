import unittest

from tender_classifier import classify_tender


class TenderClassifierTests(unittest.TestCase):
    def assert_sector(self, title: str, expected: str, description: str = "") -> None:
        result = classify_tender(title=title, description=description)
        self.assertEqual(expected, result.sector, f"{title}: {result}")

    def test_clear_subjects(self) -> None:
        fixtures = {
            "Procurement of computer workstations and network switches": "ICT & Software",
            "Supply of medical equipment and laboratory reagents": "Healthcare & Medical",
            "Construction of a classroom block": "Civil & Infrastructure",
            "Supply and installation of a solar power system": "Electrical & Energy",
            "Supply of fertilizer and seed": "General Goods & Consumables",
            "Freight forwarding and logistics services": "Services & Logistics",
            "Consultancy services to develop an infrastructure plan": "Services & Logistics",
            "Supply of home economics equipment": "General Goods & Consumables",
            "Supply of building hardware materials": "General Goods & Consumables",
            "Development of hospital information system software": "ICT & Software",
            "Supply of surgical gloves and syringes": "Healthcare & Medical",
            "Rehabilitation works for rural roads": "Civil & Infrastructure",
        }
        for title, expected in fixtures.items():
            with self.subTest(title=title):
                self.assert_sector(title, expected)

    def test_contract_does_not_match_ict(self) -> None:
        result = classify_tender(title="Contract award for assorted kitchenware")
        self.assertNotEqual("ICT & Software", result.sector)

    def test_hospital_construction_is_civil(self) -> None:
        self.assert_sector("Construction of a new district hospital", "Civil & Infrastructure")

    def test_generic_notice_is_sent_for_review(self) -> None:
        result = classify_tender(title="Invitation to bid", description="Public procurement opportunity")
        self.assertEqual("Other", result.sector)
        self.assertTrue(result.needs_review)
        self.assertLess(result.confidence, 70)

    def test_strong_title_evidence_has_high_confidence(self) -> None:
        result = classify_tender(title="Supply of computer servers and cybersecurity software licenses")
        self.assertEqual("ICT & Software", result.sector)
        self.assertFalse(result.needs_review)
        self.assertGreaterEqual(result.confidence, 85)


if __name__ == "__main__":
    unittest.main()
