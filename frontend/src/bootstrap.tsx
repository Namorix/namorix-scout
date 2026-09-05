import { mount } from "./mount"
import "./main.scss"
import "./config/coreConfig"

const el = document.getElementById("root")
if (el) await mount(el)
