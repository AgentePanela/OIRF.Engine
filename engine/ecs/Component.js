export class Component {
    constructor() {
        this.type = this.constructor.name;
    }
}

export class Transform extends Component {
    constructor(x = 0, y = 0, rotation = 0, scaleX = 1, scaleY = 1) {
        super();
        this.x = x;
        this.y = y;
        this.rotation = rotation;
        this.scaleX = scaleX;
        this.scaleY = scaleY;
    }
}

export class Velocity extends Component {
    constructor(x = 0, y = 0) {
        super();
        this.x = x;
        this.y = y;
    }
}

export class Sprite extends Component {
    constructor(color = '#ffffff', width = 32, height = 32, shape = 'rectangle') {
        super();
        this.color = color;
        this.width = width;
        this.height = height;
        this.shape = shape;
        // image//
        this.visible = true;
    }
}

export class Collider extends Component {
    constructor(width, height, isTrigger = false) {
        super();
        this.width = width;
        this.height = height;
        this.isTrigger = isTrigger;
        this.collisions = [];
    }
}

export class Input extends Component {
    constructor() {
        super();
        this.keys = new Set();
        this.mousePos = { x: 0, y: 0 };
        this.mouseButtons = new Set();
    }
}

// export class Health extends Component {     
//     constructor(maxHealth = 100) {         
//         super();         
//         this.maxHealth = maxHealth;         
//         this.currentHealth = maxHealth;     
//     } 
// }

// // class Component {}
// // const playerHealth = new Health(150);
// // console.log(`Vida atual: ${playerHealth.currentHealth}`);
// // playerHealth.currentHealth -= 30;
// // console.log(`Vida dano: ${playerHealth.currentHealth}`);
// // playerHealth.currentHealth += 20;
// // if (playerHealth.currentHealth > playerHealth.maxHealth) {
// //     playerHealth.currentHealth = playerHealth.maxHealth;
// // }
// // exemplo d componente

export class SpriteRenderer extends Component {
    constructor(spriteId = null, layer = 0) {
        super();
        this.spriteId = spriteId;
        this.layer = layer;
        this.visible = true;
        this.tint = 0xFFFFFF;
        this.alpha = 1.0;
    }
}

export class ScriptComponent extends Component {
    constructor(scriptId = null) {
        super();
        this.scriptId = scriptId;
        this.instance = null;
        this.enabled = true;
    }
}