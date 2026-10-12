"""Protect analytic cylinders while adjusting their attached drawing outlines."""
import numpy as np


def cylinder_mask(vertices,cylinders,margin=0):
    vertices=np.asarray(vertices);inside=np.zeros(len(vertices),bool)
    for c in cylinders or []:
        d=vertices-np.asarray(c['center']);r=np.hypot(d@c['u'],d@c['v'])
        inside |= (r<=c['radius']+margin)&(vertices[:,c['axis']]>=c['low']-margin)&(vertices[:,c['axis']]<=c['high']+margin)
    return inside


def protected_cylinder_vertices(mesh,cylinders,margin):
    selected=cylinder_mask(mesh.vertices,cylinders,margin)
    if selected.any():selected[np.unique(mesh.faces[selected[mesh.faces].any(axis=1)])]=True
    return selected
