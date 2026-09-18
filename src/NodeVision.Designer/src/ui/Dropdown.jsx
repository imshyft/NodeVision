import React, { useState, useEffect, useRef } from 'react';
import './Dropdown.css'; 

const Dropdown = ({
  items,
  title = "Menu"
}) => {
  const [isOpen, setIsOpen] = useState(false);
  // const [selectedItem, setSelectedItem] = useState(title);
  
  // We use this ref to attach to the outer div so we can detect outside clicks
  const dropdownRef = useRef(null);

  useEffect(() => {
    // Function to close the dropdown if the click target is outside our ref
    const handleClickOutside = (event) => {
      if (dropdownRef.current && !dropdownRef.current.contains(event.target)) {
        setIsOpen(false);
      }
    };

    // Bind the event listener
    document.addEventListener("mousedown", handleClickOutside);
    
    // Cleanup the event listener on component unmount
    return () => {
      document.removeEventListener("mousedown", handleClickOutside);
    };
  }, []);

  const toggleDropdown = () => setIsOpen(!isOpen);

  const handleItemClick = (item) => {
    // setSelectedItem(item.label);
    setIsOpen(false); // Close menu after selection

    if (item.action) {
      item.action();
    }
  };

  // const menuItems = ["Profile", "Settings", "Billing", "Logout"];

  return (
    <div className="dropdown" ref={dropdownRef}>
      <button className="dropdown-button" onClick={toggleDropdown}>
        {title}
        <span className="dropdown-arrow">{isOpen ? '▲' : '▼'}</span>
      </button>

      {isOpen && (
        <ul className="dropdown-menu">
          {items.map((item, index) => (
            <li 
              key={item.id || index} 
              className="dropdown-item" 
              onClick={() => handleItemClick(item)}
            >
              {item.label}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
};

export default Dropdown;