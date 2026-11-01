"""CPU controls for the actual shader's advection coordinates, not a rendered-look verdict."""
import argparse
import json
import math
import re
from pathlib import Path


def equations(source):
    phases = re.search(r"float ph0 = ([^,;]+), ph1 = ([^;]+);", source)
    weight = re.search(r"float w = ([^;]+);", source)
    assert phases and weight, "Flow shader timing equations missing"
    assert "float t = TIME * flow_speed * 0.25;" in source, "Recheck authored clock speed"
    assert "ph0 * 2.0" in source and "ph1 * 2.0" in source, "Recheck displacement scale"
    return phases.group(1), phases.group(2), weight.group(1)


def sample(expressions, seconds, speed):
    scope = dict(t=seconds*speed*.25, fract=lambda x:x-math.floor(x), abs=abs)
    values = [eval(expression, {"__builtins__": {}}, scope) for expression in expressions[:2]]
    scope.update(ph0=values[0], ph1=values[1])
    w = eval(expressions[2], {"__builtins__": {}}, scope)
    return [2*x for x in values], [w,1-w]


def check(source):
    expressions = equations(source)
    reports = []
    for speed in [.22,.9,1.25,1.4,1.7]:
        cycle = 4/speed
        worst = 0
        # At both resets and every rendered frame, a visible sample may only
        # travel its original .5*flow_speed. Catch a rewind without using pixels.
        for fps in [30,60,120]:
            dt = 1/fps
            for half in range(1,9):
                at = half*cycle/2
                before, weights_before = sample(expressions,at-dt/2,speed)
                after, weights_after = sample(expressions,at+dt/2,speed)
                for side in [0,1]:
                    if min(weights_before[side],weights_after[side])>.01:
                        step = after[side]-before[side]
                        assert math.isclose(step,speed*.5*dt,abs_tol=1e-10), (speed,fps,half,side,step)
                        worst = max(worst,abs(step))
            for frame in range(fps*8):
                _, weights = sample(expressions,frame/fps,speed)
                assert all(-1e-12<=w<=1+1e-12 for w in weights)
                assert math.isclose(sum(weights),1,abs_tol=1e-12)
        first, _ = sample(expressions,.17*cycle,speed)
        later, _ = sample(expressions,1.17*cycle,speed)
        assert all(math.isclose(b-a,2,abs_tol=1e-10) for a,b in zip(first,later)), "The ripple field repeats its old cycle"
        reports.append(dict(flow_speed=speed,old_reset_interval_seconds=cycle/2,
                            old_field_loop_seconds=cycle,continued_travel_per_cycle=2,
                            largest_visible_frame_step=worst))
    return reports


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source',type=Path,default=Path(__file__).resolve().parents[2]/'src/Presentation/Greybox/WaterView.cs')
    parser.add_argument('--out',type=Path)
    args = parser.parse_args()
    source = args.source.read_text(encoding='utf-8-sig')
    reports = check(source)
    old = source.replace('float ph0 = t, ph1 = t + 0.5;', 'float ph0 = fract(t), ph1 = fract(t + 0.5);').replace('float w = abs(1.0 - 2.0 * fract(t));','float w = abs(1.0 - 2.0 * ph0);')
    assert old!=source, "Baseline negative control could not be built"
    try:
        check(old)
    except AssertionError as failure:
        rejected = str(failure)
    else:
        raise AssertionError('Original visible-reset shader was not rejected')
    # Exactly retain the original first half-cycle, blend envelope, sampling
    # count, four-octave fields and all material settings; no slowed animation.
    for speed in [.22,.9,1.25,1.4,1.7]:
        for fraction in [0,.1,.2,.49]:
            assert sample(equations(source),fraction*4/speed,speed)==sample(equations(old),fraction*4/speed,speed)
    report = dict(status='PASS',rendered=False,controls='5 material speeds x 30/60/120Hz; 8 half-cycle boundaries; bounded weights; no field revisit after old period; original rejected; initial half-cycle unchanged',
                  baseline_negative_control=rejected,materials=reports,
                  limitation='Blend weights remain periodic; fields advect continuously. Actual packaged static-camera before/after video still required.')
    if args.out:args.out.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,indent=2))


if __name__=='__main__':main()
