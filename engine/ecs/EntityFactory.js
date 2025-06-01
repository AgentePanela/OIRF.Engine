import { Transform, Velocity, Sprite, Collider, Input, Health, AI } from './ecs/Component.js';

export class EntityFactory {
    constructor(world) {
        this.world = world;
    }

    createObject(x = 0, y = 0) {
        const player = this.world.createEntity('object');
        
        object.addComponent(new Transform(x, y))
              .addComponent(new Sprite('#4ade80', 32, 32, 'rectangle'))
              .addComponent(new Collider(32, 32))
              .addComponent(new Input())
              .addComponent(new Health(100));
        
        return object;
    }

    createProjectile(x = 0, y = 0, velocityX = 0, velocityY = 0) {
        const projectile = this.world.createEntity('projectile');
        
        projectile.addComponent(new Transform(x, y))
                  .addComponent(new Velocity(velocityX, velocityY))
                  .addComponent(new Sprite('#ffffff', 4, 4, 'circle'))
                  .addComponent(new Collider(4, 4, true));
        
        return projectile;
    }
}

