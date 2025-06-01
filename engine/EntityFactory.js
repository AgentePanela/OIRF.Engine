import { Transform, Velocity, Sprite, Collider, Input, Health, AI } from './ecs/Component.js';

export class EntityFactory {
    constructor(world) {
        this.world = world;
    }

    createPlayer(x = 0, y = 0) {
        const player = this.world.createEntity('player');
        
        player.addComponent(new Transform(x, y))
              .addComponent(new Sprite('#4ade80', 32, 32, 'rectangle'))
              .addComponent(new Collider(32, 32))
              .addComponent(new Input())
              .addComponent(new Health(100));
        
        return player;
    }

    createEnemy(x = 0, y = 0) {
        const enemy = this.world.createEntity('enemy');
        
        enemy.addComponent(new Transform(x, y))
             .addComponent(new Sprite('#ef4444', 24, 24, 'circle'))
             .addComponent(new Collider(24, 24))
             .addComponent(new Velocity(
                 (Math.random() - 0.5) * 100,
                 (Math.random() - 0.5) * 100
             ))
             .addComponent(new Health(50))
             .addComponent(new AI('patrol'));
        
        return enemy;
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

