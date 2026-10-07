"""Deterministic tender subject classification shared by all scraper jobs.

The classifier deliberately excludes the procuring entity and previously saved
category labels. It classifies what is being procured, not who is buying it,
and returns low confidence for notices without enough subject evidence.
"""

from __future__ import annotations

import re
import unicodedata
from dataclasses import dataclass
from typing import Mapping, Sequence


@dataclass(frozen=True)
class TenderClassification:
    sector: str
    category: str
    confidence: int
    evidence: tuple[str, ...]
    needs_review: bool


SECTOR_CATEGORIES = {
    "ICT & Software": "ICT & Software Solutions",
    "Healthcare & Medical": "Healthcare & Medical Supplies",
    "Electrical & Energy": "Electrical, Energy & Utilities",
    "Civil & Infrastructure": "Construction & Civil Infrastructure",
    "General Goods & Consumables": "General Goods, Agriculture & Supplies",
    "Services & Logistics": "Professional, Transport & Logistics Services",
}


KEYWORDS: Mapping[str, tuple[str, ...]] = {
    "ICT & Software": (
        "ict",
        "information technology",
        "software",
        "software license",
        "computer",
        "laptop",
        "server",
        "network",
        "networking",
        "database",
        "cybersecurity",
        "telecommunication",
        "telecom",
        "cloud computing",
        "erp system",
        "website development",
        "mobile application",
        "firewall",
        "data centre",
        "data center",
        "computer hardware",
        "printer",
        "photocopier",
        "structured cabling",
        "digital platform",
        "information system",
    ),
    "Healthcare & Medical": (
        "medical equipment",
        "medical supplies",
        "medical consumables",
        "healthcare equipment",
        "healthcare supplies",
        "pharmaceutical",
        "medicine",
        "medicines",
        "surgical",
        "surgical supplies",
        "hospital supplies",
        "hospital equipment",
        "clinical supplies",
        "diagnostic",
        "laboratory reagent",
        "laboratory equipment",
        "vaccine",
        "patient monitor",
        "medical device",
        "dialysis",
        "radiology",
        "x ray",
        "ultrasound",
        "ambulance",
        "blood pressure monitor",
        "oxygen concentrator",
        "medical gas",
        "syringe",
        "catheter",
        "dental equipment",
        "test kit",
    ),
    "Electrical & Energy": (
        "electrical works",
        "electrical equipment",
        "electricity",
        "solar",
        "solar panel",
        "renewable energy",
        "power supply",
        "power generation",
        "power distribution",
        "transmission line",
        "electrical grid",
        "mini grid",
        "transformer",
        "substation",
        "generator",
        "switchgear",
        "inverter",
        "photovoltaic",
        "high voltage",
        "circuit breaker",
        "smart meter",
        "power transmission",
        "electromechanical equipment",
    ),
    "Civil & Infrastructure": (
        "civil works",
        "construction works",
        "construction",
        "road works",
        "road construction",
        "bridge construction",
        "building works",
        "building construction",
        "rehabilitation works",
        "renovation works",
        "refurbishment works",
        "water infrastructure",
        "water supply works",
        "sewer works",
        "sanitation works",
        "borehole drilling",
        "drainage works",
        "earthworks",
        "roofing works",
        "paving works",
        "concrete works",
        "fencing works",
        "irrigation works",
        "consolidation works",
    ),
    "General Goods & Consumables": (
        "general goods",
        "general supplies",
        "office supplies",
        "stationery",
        "office furniture",
        "school furniture",
        "household furniture",
        "home economics equipment",
        "kitchen equipment",
        "cleaning materials",
        "cleaning supplies",
        "detergent",
        "uniform",
        "protective clothing",
        "personal protective equipment",
        "industrial ppe",
        "food supplies",
        "food products",
        "groceries",
        "agricultural inputs",
        "farming inputs",
        "fertilizer",
        "seed supply",
        "livestock",
        "animal feed",
        "printing supplies",
        "building materials",
        "tools and hardware",
        "building hardware",
        "furniture",
        "banner",
        "printing services",
        "air conditioner",
        "pipes and fittings",
        "weather equipment",
        "forensic laboratory",
        "laboratory furniture",
        "laboratory cupboards",
        "fuel supply",
        "lubricant supply",
    ),
    "Services & Logistics": (
        "consultancy",
        "consulting services",
        "professional services",
        "advisory services",
        "audit services",
        "external audit",
        "training services",
        "research services",
        "feasibility study",
        "technical assistance",
        "monitoring and evaluation",
        "project supervision",
        "design services",
        "transport services",
        "logistics services",
        "freight forwarding",
        "courier services",
        "vehicle hire",
        "car hire",
        "fleet management",
        "vehicle maintenance",
        "security services",
        "guarding services",
        "cleaning services",
        "catering services",
        "insurance services",
        "legal services",
        "recruitment services",
        "consultant",
        "advisor",
        "advisory",
        "evaluation",
        "assessment",
        "review",
        "survey",
        "capacity building",
        "editing",
        "translation",
        "resource mobilization",
        "vehicle",
        "minibus",
        "truck",
        "automotive",
    ),
}


TITLE_INTENT: Mapping[str, tuple[str, ...]] = {
    "Civil & Infrastructure": (
        "construction",
        "civil works",
        "road works",
        "building works",
        "rehabilitation works",
        "renovation works",
    ),
    "Services & Logistics": (
        "consultancy",
        "consulting services",
        "audit services",
        "external audit",
        "feasibility study",
        "technical assistance",
        "training services",
        "consultant",
        "advisor",
        "evaluation",
        "assessment",
        "review",
        "survey",
    ),
}


def _normalize(value: object) -> str:
    text = unicodedata.normalize("NFKD", str(value or ""))
    text = "".join(char for char in text if not unicodedata.combining(char))
    return re.sub(r"[^a-z0-9]+", " ", text.lower()).strip()


def _matches(text: str, phrase: str) -> bool:
    normalized = _normalize(phrase)
    if not normalized:
        return False
    return re.search(rf"(?<![a-z0-9]){re.escape(normalized)}(?![a-z0-9])", text) is not None


def _line_item_text(line_items: Sequence[object] | None) -> str:
    values: list[str] = []
    for item in line_items or ():
        if isinstance(item, Mapping):
            values.extend((str(item.get("description") or ""), str(item.get("specification") or "")))
        else:
            values.append(str(item or ""))
    return _normalize(" ".join(values))


def classify_tender(
    *,
    title: object,
    description: object = "",
    scope: object = "",
    line_items: Sequence[object] | None = None,
) -> TenderClassification:
    fields = (
        ("title", _normalize(title), 12),
        ("scope", _normalize(scope), 6),
        ("line items", _line_item_text(line_items), 5),
        ("description", _normalize(description), 2),
    )
    scores = {sector: 0.0 for sector in KEYWORDS}
    evidence: dict[str, list[str]] = {sector: [] for sector in KEYWORDS}

    for sector, phrases in KEYWORDS.items():
        for phrase in phrases:
            for field_name, text, weight in fields:
                if text and _matches(text, phrase):
                    specificity = 1 + min(0.5, max(0, len(_normalize(phrase).split()) - 1) * 0.15)
                    scores[sector] += weight * specificity
                    evidence[sector].append(f"{field_name}: {phrase}")

    title_text = fields[0][1]
    for sector, phrases in TITLE_INTENT.items():
        if any(_matches(title_text, phrase) for phrase in phrases):
            scores[sector] += 12

    ranked = sorted(scores.items(), key=lambda item: (-item[1], item[0]))
    top_sector, top_score = ranked[0]
    second_score = ranked[1][1]
    margin = top_score - second_score
    has_title_evidence = any(item.startswith("title:") for item in evidence[top_sector])
    conclusive = top_score >= 10 and has_title_evidence and margin >= 4

    if top_score == 0:
        return TenderClassification(
            sector="Other",
            category="Needs Classification Review",
            confidence=25,
            evidence=(),
            needs_review=True,
        )

    confidence = round(min(98, 60 + min(24, top_score * 1.1) + min(14, max(0, margin))))
    if not conclusive:
        confidence = min(confidence, 69)

    unique_evidence = tuple(dict.fromkeys(evidence[top_sector]))[:5]
    return TenderClassification(
        sector=top_sector,
        category=SECTOR_CATEGORIES[top_sector],
        confidence=confidence,
        evidence=unique_evidence,
        needs_review=not conclusive,
    )


def classification_payload(result: TenderClassification) -> dict[str, object]:
    return {
        "classificationVersion": "tender-rules-v2",
        "classificationSource": "rules",
        "sector": result.sector,
        "category": result.category,
        "categoryNames": [result.category],
        "aiScore": result.confidence,
        "classificationConfidence": result.confidence,
        "classificationEvidence": list(result.evidence),
        "needsClassificationReview": result.needs_review,
    }
