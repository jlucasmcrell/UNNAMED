"""The chatter rules, read from content/chatter and mirrored from the WP16 C# lint (CHT002-CHT011).

The C# lint is the gate; this module exists so lines can be checked while they are being written instead of after a
build. Every rule here is a copy of the corresponding check in src/Content/ChatterContent.cs, not a strengthening of it:

  CHT003 key formats, CHT004 tags the schema names, CHT005 slots and the events they wait on,
  CHT006 at most limits.max_chars, CHT007 no raw ids, CHT008 proper nouns only from the glossary,
  CHT009 no banned term, CHT010 no near-duplicate, CHT011 corpus lines carry ai_generated: true from a known batch.
"""

import re
from pathlib import Path

import yaml

PLACEHOLDER = re.compile(r"\{([^{}]*)\}")
SNAKE_CASE = re.compile(r"\b[a-z][a-z0-9]*_[a-z0-9_]+\b")
INSTANCE_ID = re.compile(r"\b(?:itm|npc|crt|bld|cnt|qst|crp|anc|smn|plt|wev|chr)_[0-9A-Za-z]{6,}\b")
WORD = re.compile(r"[A-Za-z][A-Za-z'\u2019-]*")
SENTENCE_BREAK = set('.!?:;"\u201c\u2014')


def load_meta(content_root):
    """The schema, the glossary's allowed words and the banned terms, read from content/chatter."""
    chatter = Path(content_root) / "chatter"
    schema_doc = yaml.safe_load((chatter / "_schema.yaml").read_text(encoding="utf-8"))
    glossary_doc = yaml.safe_load((chatter / "_glossary.yaml").read_text(encoding="utf-8"))
    banned_doc = yaml.safe_load((chatter / "_banned.yaml").read_text(encoding="utf-8"))
    return ChatterSchema(schema_doc, glossary_doc), Banned(banned_doc)


class ChatterSchema:
    def __init__(self, doc, glossary_doc):
        self.doc = doc
        self.speakers = sorted(doc["speaker_types"])
        self.places = list(doc["places"])
        self.times = list(doc["time_bands"])
        self.weather = list(doc["weather"])
        self.events = sorted(doc["events"])
        self.slots = doc["placeholders"]
        self.uses = doc["templates"]
        self.max_chars = doc["limits"]["max_chars"]
        self.near_duplicate_jaccard = doc["limits"]["near_duplicate_jaccard"]
        self.coverage_min_lines = doc["limits"]["coverage_min_lines"]
        self.corpus_key = re.compile(doc["line_key"]["corpus"])
        self.template_key = re.compile(doc["line_key"]["template"])
        words = set()
        references = []
        for entry in glossary_doc["terms"]:
            for match in WORD.finditer(entry["term"]):
                words.add(possessive(match.group(0)))
            if "ref" in entry:
                references.append((entry["term"], entry["ref"]))
        self.glossary_words = frozenset(words)
        self.glossary_references = references

    def slot_events(self, name):
        slot = self.slots[name]
        requires = slot.get("requires_event") or []
        return [requires] if isinstance(requires, str) else list(requires)

    def corpus_slot(self, name):
        return self.slots[name].get("corpus") is True

    def cells(self):
        """Every speaker x place x time-band cell the coverage report counts."""
        return [(s, p, t) for s in self.speakers for p in self.places for t in self.times]


class Banned:
    def __init__(self, doc):
        self.terms = [(category, term) for category, terms in doc["categories"].items() for term in terms if term.strip()]
        self.patterns = [(category, term, self._pattern(term)) for category, term in self.terms]

    @staticmethod
    def _pattern(term):
        body = r"[\s-]+".join(re.escape(part) for part in term.split())
        return re.compile(r"(?<![A-Za-z])" + body + r"(?![A-Za-z])", re.IGNORECASE)

    def hits(self, text):
        return [(category, term) for category, term, pattern in self.patterns if pattern.search(text)]


def possessive(word):
    return word[:-2] if word.endswith("'s") or word.endswith("\u2019s") else word


def strip_slots(text):
    return PLACEHOLDER.sub(" ", text)


def proper_nouns(text):
    """The capitalised words of a text that do not begin a sentence: what a proper noun would be."""
    found = []
    sentence_start = True
    last = 0
    for match in WORD.finditer(text):
        between = text[last:match.start()]
        if any(char in SENTENCE_BREAK for char in between) or " - " in between:
            sentence_start = True
        last = match.end()
        word = match.group(0).rstrip("'\u2019-")
        if word[0].isupper() and not sentence_start and not is_pronoun_i(word):
            found.append(possessive(word))
        sentence_start = False
    return found


def is_pronoun_i(word):
    return word == "I" or (len(word) > 2 and word[0] == "I" and word[1] in "'\u2019")


def bigrams(text):
    words = [match.group(0).strip("'\u2019-") for match in WORD.finditer(strip_slots(text).lower())]
    words = [word for word in words if word]
    if len(words) < 2:
        return set(words)
    return {a + " " + b for a, b in zip(words, words[1:])}


def check_line(line, schema, banned, batches):
    """Every finding the lint would report for one line: (code, message) pairs."""
    found = []
    key, text = line["key"], line["text"]
    if not schema.corpus_key.match(key):
        found.append(("CHT003", f"{key} is not a corpus key"))
    if key.split(".")[1] != line.get("batch"):
        found.append(("CHT003", f"{key} is not of the file's batch {line.get('batch')}"))
    for dimension, known in (("speakers", schema.speakers), ("places", schema.places), ("times", schema.times),
                             ("weather", schema.weather), ("events", schema.events)):
        for tag in line.get(dimension) or []:
            if tag not in known:
                found.append(("CHT004", f"{key} is tagged {dimension[:-1]} '{tag}', which the schema does not name"))
    if line.get("channel", "local") not in ("local", "world"):
        found.append(("CHT004", f"{key} is said on '{line.get('channel')}'; a corpus line is local or world"))
    if "{" in strip_slots(text) or "}" in strip_slots(text):
        found.append(("CHT005", f"{key} has a brace that is not a {{slot}}"))
    for match in PLACEHOLDER.finditer(text):
        name = match.group(1)
        if name not in schema.slots:
            found.append(("CHT005", f"{key} uses {{{name}}}, which is not a placeholder the schema names"))
            continue
        if not schema.corpus_slot(name):
            found.append(("CHT005", f"{key} uses {{{name}}}, which only templates may use"))
            continue
        required = schema.slot_events(name)
        if required and not [event for event in required if event in (line.get("events") or [])]:
            found.append(("CHT005", f"{key} uses {{{name}}}, so it waits on {' or '.join(required)}"))
    if len(text) > schema.max_chars:
        found.append(("CHT006", f"{key} is {len(text)} characters; a line is at most {schema.max_chars}"))
    words = strip_slots(text)
    if SNAKE_CASE.search(words):
        found.append(("CHT007", f"{key} says '{SNAKE_CASE.search(words).group(0)}', which reads as an ID, not a word"))
    elif INSTANCE_ID.search(words):
        found.append(("CHT007", f"{key} says '{INSTANCE_ID.search(words).group(0)}', an instance ID"))
    for noun in sorted(set(noun for noun in proper_nouns(words) if noun not in schema.glossary_words)):
        found.append(("CHT008", f"{key} names '{noun}', which is not in the glossary"))
    for category, term in banned.hits(words):
        found.append(("CHT009", f"{key} says '{term}' ({category.replace('_', ' ')})"))
    if not line.get("ai_generated") or line.get("batch") not in batches:
        found.append(("CHT011", f"{key} is a corpus line, so it is ai_generated: true from a recorded batch"))
    return found


def near_duplicates(lines, threshold):
    """Pairs of lines whose word-bigram sets overlap at or above the threshold, Jaccard (CHT010)."""
    grams = [bigrams(line["text"]) for line in lines]
    found = []
    for i in range(len(lines)):
        for j in range(i + 1, len(lines)):
            small, large = sorted((len(grams[i]), len(grams[j])))
            if large == 0 or small / large < threshold:
                continue
            shared = len(grams[i] & grams[j])
            jaccard = shared / (len(grams[i]) + len(grams[j]) - shared)
            if jaccard >= threshold:
                found.append((lines[i]["key"], lines[j]["key"], round(jaccard, 2)))
    return found


def fits(tags, value):
    return not tags or value in tags


def coverage(lines, schema):
    """The thin cells: how many event-free lines fit each speaker x place x time-band cell."""
    always = [line for line in lines if not (line.get("events") or [])]
    thin = []
    for speaker, place, band in schema.cells():
        count = sum(1 for line in always
                    if fits(line.get("speakers"), speaker)
                    and fits(line.get("places"), place)
                    and fits(line.get("times"), band))
        if count < schema.coverage_min_lines:
            thin.append((speaker, place, band, count))
    return thin
