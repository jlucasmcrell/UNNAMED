"""Measured cross-section envelopes for a chest over actual native underlayers."""
import numpy as np
from mathutils import Vector


def hull(points):
    points=sorted(set(tuple(p) for p in points))
    def cross(o,a,b):return (a[0]-o[0])*(b[1]-o[1])-(a[1]-o[1])*(b[0]-o[0])
    lower=[];upper=[]
    for p in points:
        while len(lower)>1 and cross(lower[-2],lower[-1],p)<=0:lower.pop()
        lower.append(p)
    for p in reversed(points):
        while len(upper)>1 and cross(upper[-2],upper[-1],p)<=0:upper.pop()
        upper.append(p)
    return np.array(lower[:-1]+upper[:-1])


class Envelope:
    def __init__(self,points,origin,axis,low,high,width,band=.018):
        self.origin=np.array(origin);self.axis=np.array(axis.normalized())
        side=axis.normalized().cross(Vector((0,1,0))).normalized()
        self.u=np.array(side);self.v=np.cross(self.axis,self.u)
        delta=points-self.origin;along=delta@self.axis
        radial=np.column_stack((delta@self.u,delta@self.v))
        keep=np.linalg.norm(radial,axis=1)<width
        self.rings={}
        self.low=low;self.high=high;self.width=width
        for distance in np.arange(low,high+.01,.01):
            ring=radial[keep&(abs(along-distance)<band)]
            if len(ring)<4:continue
            boundary=hull(ring)
            if len(boundary)>=3:self.rings[round(float(distance),2)]=boundary
        assert self.rings,'Native underlayer envelope has no cross-sections'

    def fit(self,point,clearance):
        delta=np.array(point)-self.origin;t=float(delta@self.axis)
        if t<self.low-.03 or t>self.high+.03:return point
        ring=self.rings[min(self.rings,key=lambda d:abs(d-t))]
        center=ring.mean(axis=0);p=np.array([delta@self.u,delta@self.v])
        direction=p-center;radius=float(np.linalg.norm(direction))
        if radius<1e-5:return point
        direction/=radius;exits=[]
        def cross(a,b):return a[0]*b[1]-a[1]*b[0]
        for a,b in zip(ring,np.roll(ring,-1,axis=0)):
            edge=b-a;denominator=cross(direction,edge)
            if abs(denominator)<1e-8:continue
            offset=a-center;distance=cross(offset,edge)/denominator
            fraction=cross(offset,direction)/denominator
            if distance>=0 and -.0001<=fraction<=1.0001:exits.append(distance)
        if not exits:return point
        required=max(exits)+clearance
        if radius>=required:return point
        q=center+direction*required
        return Vector(self.origin+self.axis*t+self.u*q[0]+self.v*q[1])

    def fit_array(self,points,clearance):
        """The same radial hull fit, grouped by cross-section for batch authoring."""
        result=points.copy();delta=points-self.origin;along=delta@self.axis
        distances=np.array(list(self.rings));keys=np.abs(along[:,None]-distances).argmin(axis=1)
        for key,distance in enumerate(distances):
            indices=np.flatnonzero((keys==key)&(along>=self.low-.03)&(along<=self.high+.03))
            if not len(indices):continue
            ring=self.rings[distance];center=ring.mean(axis=0)
            radial=np.column_stack((delta[indices]@self.u,delta[indices]@self.v))-center
            radius=np.linalg.norm(radial,axis=1);direction=radial/np.maximum(radius[:,None],1e-20)
            edge=np.roll(ring,-1,axis=0)-ring;offset=ring-center
            denominator=direction[:,None,0]*edge[None,:,1]-direction[:,None,1]*edge[None,:,0]
            valid=np.abs(denominator)>=1e-8;safe=np.where(valid,denominator,1)
            exits=(offset[:,0]*edge[:,1]-offset[:,1]*edge[:,0])[None,:]/safe
            fraction=(offset[None,:,0]*direction[:,None,1]-offset[None,:,1]*direction[:,None,0])/safe
            exits=np.where(valid&(exits>=0)&(fraction>=-.0001)&(fraction<=1.0001),exits,-np.inf)
            padding=np.asarray(clearance)[indices] if np.ndim(clearance) else clearance
            required=exits.max(axis=1)+padding;move=(radius>1e-5)&(radius<required)
            q=center+direction[move]*required[move,None]
            result[indices[move]]=self.origin+along[indices[move],None]*self.axis+q[:,0,None]*self.u+q[:,1,None]*self.v
        return result

    def source_depth(self,points,region=None):
        """Retain a source's nested cloth/plate depths when moving its inner shell."""
        delta=points-self.origin;along=delta@self.axis
        radial=np.column_stack((delta@self.u,delta@self.v));result=np.zeros(len(points))
        distances=np.array(list(self.rings));keys=np.abs(along[:,None]-distances).argmin(axis=1)
        for key,distance in enumerate(distances):
            eligible=(along>=self.low-.03)&(along<=self.high+.03)
            if region is not None:eligible&=region
            selected=np.flatnonzero((keys==key)&eligible)
            if not len(selected):continue
            center=self.rings[distance].mean(axis=0)
            around=radial-center;radius=np.linalg.norm(around,axis=1)
            selected=selected[radius[selected]<self.width]
            if not len(selected):continue
            angle=(np.arctan2(around[:,1],around[:,0])+2*np.pi)%(2*np.pi)
            bins=np.floor(angle*32/(2*np.pi)).astype(int)%32
            nearby=(abs(along-distance)<.035)&(radius>.012)&(radius<self.width)&eligible
            inner=np.full(32,np.nan)
            for sector in range(32):
                difference=np.minimum((bins-sector)%32,(sector-bins)%32)
                samples=radius[nearby&(difference<=1)]
                if len(samples):inner[sector]=np.quantile(samples,.05)
            known=np.flatnonzero(np.isfinite(inner))
            if not len(known):continue
            for sector in np.flatnonzero(~np.isfinite(inner)):
                nearest=known[np.minimum((known-sector)%32,(sector-known)%32).argmin()]
                inner[sector]=inner[nearest]
            inner=(np.roll(inner,1)+2*inner+np.roll(inner,-1))/4
            position=angle[selected]*32/(2*np.pi);sector=np.floor(position).astype(int)%32;fraction=position-np.floor(position)
            baseline=inner[sector]*(1-fraction)+inner[(sector+1)%32]*fraction
            result[selected]=np.maximum(0,radius[selected]-baseline)
        return result


def anatomical_regions(human,points,skin_regions=False):
    """Partition authoring references by geometric torso/limb distance, never hide geometry."""
    if skin_regions:
        import bpy
        from mathutils.kdtree import KDTree
        original=human.objects.body;body=original.evaluated_get(bpy.context.evaluated_depsgraph_get())
        assert len(body.data.vertices)==len(original.data.vertices)
        names=[g.name for g in original.vertex_groups];bone_names=set(human.objects.rig.data.bones.keys());labels=[];tree=KDTree(len(body.data.vertices))
        for vertex,source in zip(body.data.vertices,original.data.vertices):
            tree.insert(body.matrix_world@vertex.co,vertex.index);scores=np.zeros(5)
            for group in source.groups:
                name=names[group.group]
                if name not in bone_names:continue
                side=1 if '.L' in name else 3 if '.R' in name else 0
                code=(side+1 if any(n in name for n in ('forearm','hand','finger','f_')) else side) if side and any(n in name for n in ('upper_arm','shoulder','forearm','hand','finger','f_')) else 0
                scores[code]+=group.weight
            labels.append(int(scores.argmax()))
        tree.balance();return np.array([labels[tree.find(Vector(point))[1]] for point in points],dtype=int)
    rig=human.objects.rig;landmark=lambda name:rig.matrix_world@rig.pose.bones[name].head
    pelvis=landmark('spine');body=human.objects.body.evaluated_get(__import__('bpy').context.evaluated_depsgraph_get())
    skin=np.array([list(body.matrix_world@v.co) for v in body.data.vertices])
    # Hands can overlap the pelvis height in the native authoring pose.
    # Measure the central torso, excluding those separate limb surfaces.
    waist=skin[(abs(skin[:,2]-pelvis.z)<.07)&(abs(skin[:,0])<.23)]
    width=max(.12,float(abs(waist[:,0]).max()));depth=max(.08,float(abs(waist[:,1]-pelvis.y).max()))
    torso_radius=np.sqrt(points[:,0]**2+(points[:,1]-pelvis.y)**2)
    direction_radius=1/np.sqrt((points[:,0]/np.maximum(torso_radius,1e-9)/width)**2+((points[:,1]-pelvis.y)/np.maximum(torso_radius,1e-9)/depth)**2+1e-20)
    best=torso_radius-direction_radius
    regions=np.zeros(len(points),dtype=int);code=0
    for side in ('L','R'):
        for start,end,limit in [('upper_arm','forearm',.10),('forearm','hand',.08)]:
            code+=1;origin=landmark(start+'.'+side);axis=np.array(landmark(end+'.'+side)-origin)
            delta=skin-np.array(origin);t=delta@axis/float(axis@axis);radius=np.linalg.norm(delta-t[:,None]*axis,axis=1)
            sample=radius[(t>.35)&(t<.65)&(radius<limit)]
            assert len(sample)>10,'Native limb reference missing'
            scale=max(.025,float(np.quantile(sample,.5)))
            delta=points-np.array(origin);t=delta@axis/float(axis@axis);radius=np.linalg.norm(delta-t[:,None]*axis,axis=1)
            metric=radius-scale;choose=(t>-.04)&(t<1.04)&(metric<best)
            regions[choose]=code;best[choose]=metric[choose]
    return regions


def fit_chest_envelopes(human,obj,points,preserve_source_depth=False,proper_regions=False,clearance=.007,skin_regions=False):
    rig=human.objects.rig
    landmark=lambda name:rig.matrix_world@rig.pose.bones[name].head
    pelvis=landmark('spine');neck=landmark('neck')
    regions=anatomical_regions(human,points,skin_regions) if proper_regions else None
    torso_points=points[regions==0] if proper_regions else points[(abs(points[:,0])<.27)|(points[:,2]<pelvis.z+.05)]
    torso=Envelope(torso_points,Vector((0,0,0)),Vector((0,0,1)),pelvis.z-.28,neck.z+.05,.40,band=.035)
    arms={}
    for side_index,side in enumerate(('L','R')):
        shoulder,elbow,wrist=[landmark(name+'.'+side) for name in ('upper_arm','forearm','hand')]
        upper=points[regions==side_index*2+1] if proper_regions else points
        lower=points[regions==side_index*2+2] if proper_regions else points
        arms[side]=[(shoulder,elbow,Envelope(upper,shoulder,elbow-shoulder,-.03,(elbow-shoulder).length+.03,.13,band=.025)),
                    (elbow,wrist,Envelope(lower,elbow,wrist-elbow,-.03,(wrist-elbow).length+.03,.12,band=.025))]
    before=np.empty(len(obj.data.vertices)*3);obj.data.vertices.foreach_get('co',before);before=before.reshape((-1,3))
    depths={}
    source_regions=None
    if proper_regions:
        position_attribute=obj.data.attributes.get('p2_source_position')
        if position_attribute is None:
            position_attribute=obj.data.attributes.new('p2_source_position','FLOAT_VECTOR','POINT')
            position_attribute.data.foreach_set('vector',before.ravel())
        source_positions=np.empty(len(obj.data.vertices)*3)
        position_attribute.data.foreach_get('vector',source_positions)
        source_regions=anatomical_regions(human,source_positions.reshape((-1,3)),skin_regions)
    if preserve_source_depth:
        source_regions=source_regions if proper_regions else None
        for name,envelope in [('torso',torso)]+[(side+str(i),e) for side,values in arms.items() for i,(_,_,e) in enumerate(values)]:
            attribute=obj.data.attributes.get('p2_depth_'+name)
            if attribute is None:
                attribute=obj.data.attributes.new('p2_depth_'+name,'FLOAT','POINT')
                region=(abs(before[:,0])<.27)|(before[:,2]<pelvis.z+.05) if name=='torso' else None
                if proper_regions:
                    code={'torso':0,'L0':1,'L1':2,'R0':3,'R1':4}[name];region=source_regions==code
                attribute.data.foreach_set('value',envelope.source_depth(before,region))
            values=np.empty(len(obj.data.vertices));attribute.data.foreach_get('value',values);depths[name]=values
    fitted=before.copy()
    for iteration in range(3):
        if proper_regions:
            selected=source_regions==0
            padding=clearance+depths['torso'][selected] if preserve_source_depth else clearance
            fitted[selected]=torso.fit_array(fitted[selected],padding)
        else:fitted=torso.fit_array(fitted,clearance+depths.get('torso',0))
        for side,sign in [('L',1),('R',-1)]:
            for arm_index,(start,end,envelope) in enumerate(arms[side]):
                axis=np.array(end-start);delta=fitted-np.array(start);t=delta@axis/float(axis@axis)
                radial=np.linalg.norm(delta-t[:,None]*axis,axis=1)
                selected=(fitted[:,0]*sign>.16)&(t>-.08)&(t<1.08)&(radial<.18)
                if proper_regions:selected&=np.isin(source_regions,([1,2] if side=='L' else [3,4]))
                padding=clearance+depths[side+str(arm_index)][selected] if preserve_source_depth else clearance
                fitted[selected]=envelope.fit_array(fitted[selected],padding)
    obj.data.vertices.foreach_set('co',fitted.ravel())
    adjusted=np.flatnonzero(np.linalg.norm(fitted-before,axis=1)>1e-6).tolist()
    obj.data.update()
    return dict(method='Measured convex cross-sections of actual body, four fitted leg pieces and native office underlayers; source UV/detail retained',
                clearance_target_m=clearance,minimum_required_m=.0035,adjusted_source_vertices=adjusted,
                torso_rings=len(torso.rings),arm_rings={s:[len(e.rings) for _,_,e in a] for s,a in arms.items()},
                source_internal_depths_preserved=preserve_source_depth,
                anatomical_reference_regions=proper_regions,native_skin_region_assignment=skin_regions,
                source_depth_ranges_m={name:[float(v.min()),float(v.max())] for name,v in depths.items()})
