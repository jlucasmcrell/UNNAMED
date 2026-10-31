"""Native clothing fit controls, including face proportions for fitted headwear."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from keep_humgen_clothing_keys import main as clothing_keys
STAGE='before_meshes'

def main(context,human):
    clothing_keys(context,human,include_face=True)
